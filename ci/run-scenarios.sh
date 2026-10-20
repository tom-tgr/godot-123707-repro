#!/usr/bin/env bash
# Runs every scenario of godot-project/ in real Godot (headless) and checks each outcome against what
# godotengine/godot#123707 predicts. Exits non-zero only if a documented failure (crash or error) does NOT
# happen any more - after a Godot update that may mean the bug was fixed. A crash in a scenario expected to be
# clean is reported as a warning ("also crashed"), not a failure.
#
# Usage: ci/run-scenarios.sh <path to Godot .NET binary>
# Outcomes: crash   = process aborted (non-zero exit, unhandled exception)
#           error   = game kept running, "Disposable not registered" in the log
#           clean   = game kept running, nothing logged
set -uo pipefail

GODOT=$1
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PROJECT="$ROOT/godot-project"
# Git Bash on Windows: hand the Windows-native Godot binary a Windows path.
command -v cygpath > /dev/null && PROJECT=$(cygpath -m "$PROJECT")
LOGS="$ROOT/ci/logs"
SUMMARY=${GITHUB_STEP_SUMMARY:-/dev/stdout}
mkdir -p "$LOGS"

# scenario | seconds | expected outcome | what the game code does
SCENARIOS=(
  "inventory|15|crash|RefCounted subclass disposes its Variant fields in Dispose(bool) without checking disposing"
  "inventory-careful|15|clean|same, but only disposes when disposing == true"
  "plain-fields|15|clean|Variant fields from dictionary lookups, metadata and Call(); never disposed"
  "threads|15|clean|4 workers parse JSON into Variants and CallDeferred them to the main thread"
  "quit|0|clean|quit with 30k live Variants and pending garbage"
  "native-held|15|clean|objects with Variant fields kept alive only by the engine, released a frame later (no Dispose calls)"
  "snippet-main|5|error|reporter's snippet (dispose two copies of one Variant) inside a Godot callback"
  "snippet-task|5|clean|reporter's snippet on a Task.Run worker: the exception is swallowed, nothing is logged"
  "snippet-thread|5|crash|reporter's snippet on a plain Thread"
)

{
  echo "### godotengine/godot#123707 in real Godot"
  echo
  echo "- Godot $("$GODOT" --version 2>/dev/null | tail -1), $(uname -sm), .NET SDK $(dotnet --version)"
  echo
  echo "| Scenario | What the game code does | Expected | Actual | Exit | |"
  echo "|---|---|---|---|---:|---|"
} >> "$SUMMARY"

failures=0
details=""
for entry in "${SCENARIOS[@]}"; do
  IFS='|' read -r name seconds expected description <<< "$entry"
  log="$LOGS/$name.log"
  args=(--headless --path "$PROJECT" -- "$name")
  [ "$seconds" != "0" ] && args+=("$seconds")

  "$GODOT" "${args[@]}" > "$log" 2>&1
  code=$?

  if [ "$code" -ne 0 ]; then
    actual=crash
  elif grep -q "Disposable not registered" "$log"; then
    actual=error
  else
    actual=clean
  fi

  if [ "$actual" = "$expected" ]; then
    mark="✅"
  elif [ "$expected" = "clean" ]; then
    mark="⚠️ also crashed"   # not predicted by the issue: worth investigating, not a failed reproduction
  else
    mark="❌ did not reproduce"
    failures=$((failures + 1))
  fi
  echo "| \`$name\` | $description | $expected | **$actual** | $code | $mark |" >> "$SUMMARY"

  if [ "$actual" != "clean" ]; then
    # Godot prints the stack twice (its own error log and the runtime's); keep the first copy only.
    stack=$(grep -m1 -E "System\.[A-Za-z]+Exception|FATAL" "$log"
      awk '/^   at / { if ($0 ~ /UnregisterDisposable/ && seen++) exit; print }' "$log" | head -8 |
        sed -E 's/ in .*(\/[^/]+:line [0-9]+)$/  \1/')
    details+=$'\n'"<details><summary><code>$name</code>: $actual</summary>"$'\n\n```\n'"$stack"$'\n```\n</details>\n'
  fi
  echo "$name: expected=$expected actual=$actual exit=$code $mark"
done

runtime=$(grep -ohE "dotnet=[0-9.]+" "$LOGS"/*.log | sort -u | tr '\n' ' ')
{
  echo
  echo "Runtime the game actually ran on (from the game's own log): \`${runtime:-unknown}\`"
  echo
  echo "$details"
  echo
  if [ "$failures" -eq 0 ]; then
    echo "Every documented failure reproduced."
  else
    echo "$failures documented failure(s) did not reproduce (bug fixed, or the repro needs updating)."
  fi
} >> "$SUMMARY"

exit "$failures"
