#!/usr/bin/env bash
# Runs every scenario of godot-project/ in real Godot (headless) and checks each outcome against what
# godotengine/godot#123707 predicts. Exits non-zero if any scenario behaves differently - so a green run means
# "the bug reproduces exactly as documented", and a red run after a Godot update may mean it was fixed.
#
# Usage: ci/run-scenarios.sh <path to Godot .NET binary>
# Outcomes: crash   = process aborted (non-zero exit, unhandled exception)
#           error   = game kept running, "Disposable not registered" in the log
#           clean   = game kept running, nothing logged
set -uo pipefail

GODOT=$1
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PROJECT="$ROOT/godot-project"
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
  echo "- $("$GODOT" --version 2>/dev/null | tail -1), $(uname -sm), .NET $(dotnet --version)"
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

  if [ "$actual" = "$expected" ]; then mark="✅"; else mark="❌"; failures=$((failures + 1)); fi
  echo "| \`$name\` | $description | $expected | **$actual** | $code | $mark |" >> "$SUMMARY"

  if [ "$actual" != "clean" ]; then
    # Godot prints the stack twice (its own error log and the runtime's); keep the first copy only.
    stack=$(grep -m1 -E "System.ArgumentException" "$log"
      awk '/^   at / { if ($0 ~ /UnregisterDisposable/ && seen++) exit; print }' "$log" | head -8 |
        sed -E 's/ in .*(\/[^/]+:line [0-9]+)$/  \1/')
    details+=$'\n'"<details><summary><code>$name</code>: $actual</summary>"$'\n\n```\n'"$stack"$'\n```\n</details>\n'
  fi
  echo "$name: expected=$expected actual=$actual exit=$code $mark"
done

{
  echo
  echo "$details"
  echo
  if [ "$failures" -eq 0 ]; then
    echo "All scenarios behaved as documented: the bug reproduces."
  else
    echo "$failures scenario(s) behaved differently than documented (bug fixed, or the repro needs updating)."
  fi
} >> "$SUMMARY"

exit "$failures"
