# Repro for godotengine/godot#123707

[godotengine/godot#123707](https://github.com/godotengine/godot/issues/123707): a released Godot 4.7.x .NET game
crashes (SIGABRT) with:

```
System.ArgumentException: Disposable not registered. (Parameter 'weakReference')
  at DisposablesTracker.UnregisterDisposable(...)
  at Disposer.Dispose(bool disposing)
  at Disposer.Finalize()
  at GC.RunFinalizers()
```

This repo reproduces it with the **official Godot 4.7.2 .NET build** and ordinary game code. It doesn't copy or
change any Godot code, and it doesn't force garbage collection.

## The problem in short

`Variant.Disposer.Dispose()` is not safe to call twice. The second call frees the native value again and then throws
"Disposable not registered". On the finalizer thread or a normal `Thread`, nobody can catch that exception, so the
game crashes.

`GodotObject.Dispose(bool)` already protects against this with `if (_disposed) return;`. `Variant.Disposer` does not.
`Godot.Collections.Dictionary` has the same problem, and probably `Array`, `StringName` and `NodePath` too.

## What is in the repo

- `godot-project/`: a normal Godot 4.7 C# project. `Main.cs` runs one scenario for N seconds and quits.
- `ci/run-scenarios.sh`: runs every scenario, compares each result with the expected one, writes a summary table.
- `.github/workflows/repro.yml`: downloads official Godot and runs everything on Linux, macOS and Windows.

## Scenarios

| Scenario | What the game code does | Expected |
|---|---|---|
| `inventory` | A `RefCounted` class disposes its `Variant` fields in `Dispose(bool)` without checking `disposing`. Objects are dropped and left to the finalizer. | **crash** |
| `inventory-careful` | Same, but only disposes when `disposing == true` | no crash |
| `plain-fields` | `Variant` fields from dictionary lookups, metadata and `Call()`, never disposed | no crash |
| `threads` | 4 worker threads parse JSON into `Variant`s and send them to the main thread with `CallDeferred`. No `Dispose` anywhere. | no crash (but see below) |
| `quit` | Quit while 30k `Variant`s are alive | no crash |
| `native-held` | Objects with `Variant` fields kept alive only by the engine (Godot `Array`, metadata, a signal). No `Dispose` calls. | no crash |
| `snippet-main` | The reporter's snippet (dispose two copies of one `Variant`) on the main thread | **error in the log**, game keeps running |
| `snippet-task` | The same snippet inside `Task.Run` | nothing logged (the task swallows the exception), but the native value is still freed twice |
| `snippet-thread` | The same snippet on a normal `Thread` | **crash** |

## Results

On Linux, macOS (Apple Silicon) and Windows, with .NET 9.0.21 and 10.0.13, every expected crash and error
reproduces. Exit code 134 on Linux and macOS; Windows reports the same crash as 127.

Stack from `inventory`:

```
System.ArgumentException: Disposable not registered. (Parameter 'weakReference')
   at Godot.DisposablesTracker.UnregisterDisposable(...)  DisposablesTracker.cs:line 91
   at Godot.Variant.Disposer.Dispose(Boolean disposing)  Variant.cs:line 47
   at Godot.Variant.Disposer.Dispose()                    Variant.cs:line 37
   at Godot.Variant.Dispose()                             Variant.cs:line 103
   at ItemData.Dispose(Boolean disposing)                 InventoryScenario.cs:line 23
   at Godot.GodotObject.Finalize()                        GodotObject.base.cs:line 100
   at System.GC.RunFinalizers()
```

### Crashes without any Dispose in game code

Sometimes `threads` also crashes, even though the game code never calls `Dispose`. The stack has the same shape as in
the issue, but comes from `Dictionary`:

```
System.ArgumentException: Disposable not registered. (Parameter 'weakReference')
   at Godot.DisposablesTracker.UnregisterDisposable(...)        DisposablesTracker.cs:line 91
   at Godot.Collections.Dictionary.Dispose(Boolean disposing)  Dictionary.cs:line 71
   at Godot.Collections.Dictionary.Finalize()                   Dictionary.cs:line 52
   at System.GC.RunFinalizers()
```

Seen twice so far, once on Linux and once on macOS. What disposes the `Dictionary` the first time is not known yet.
Godot's own C# code only disposes native `godot_variant` values, not managed `Variant`s, so it is not an obvious
`Dispose()` call in the engine.

### A different crash at shutdown (Linux only)

On Linux, a scenario sometimes finishes normally and then Godot crashes while quitting:

```
FATAL: Condition "handle.value && GDMono::get_singleton() == nullptr" is true.
   at: release (modules/mono/mono_gc_handle.cpp:38)
Leaked unsafe reference to object: <RefCounted#...>   (many times)
```

Seen 4 times, in 4 different scenarios, on .NET 9 and 10. This looks like a separate bug.

## Run it yourself

```bash
cd godot-project
dotnet build
Godot --headless --path . --import
Godot --headless --path . -- inventory 15        # or any scenario from the table
../ci/run-scenarios.sh /path/to/Godot           # all scenarios with a summary
```

Note about .NET versions: when a project runs from the Godot editor binary, Godot starts .NET through its own
`GodotPlugins.runtimeconfig.json` (`rollForward: LatestMajor`). So it uses the newest .NET it can find, not the one
the project targets. The workflow gives Godot a folder with only the wanted runtime (via `DOTNET_ROOT`) and checks
the version in each game's log.
