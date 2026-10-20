# Repro for godotengine/godot#123707

[godotengine/godot#123707](https://github.com/godotengine/godot/issues/123707): a released Godot 4.7.x .NET game aborts
(SIGABRT) with

```
System.ArgumentException: Disposable not registered. (Parameter 'weakReference')
  at DisposablesTracker.UnregisterDisposable(...)
  at Disposer.Dispose(bool disposing)
  at Disposer.Finalize()
  at GC.RunFinalizers()
```

## Real Godot 4.7.2 (official .NET build), ordinary game code

`godot-project/` is a normal Godot 4.7 C# project. It doesn't copy or touch Godot internals and doesn't force GC
timing; collections happen from everyday allocations. Run headless:

```bash
cd godot-project && dotnet build
Godot --headless --path . --import
Godot --headless --path . -- <scenario> [seconds]
```

Results on macOS arm64, Godot v4.7.2.stable.mono.official.ed1daf0bf, .NET 10.0.0:

| Scenario | Result |
|---|---|
| `inventory`: a `RefCounted` subclass disposes its `Variant` fields in a `Dispose(bool)` override without checking `disposing`; objects are dropped | **abort, exit 134, every run** |
| `inventory-careful`: same, but only disposes when `disposing == true` | no crash (1.4M objects, 20 s) |
| `plain-fields`: `Variant` fields from dictionary lookups, metadata and `Call()`; never disposed | no crash (1.4M objects) |
| `threads`: 4 workers parse JSON into `Variant`s and `CallDeferred` them to the main thread | no crash (974k `Variant`s, 62k GCs) |
| `quit`: quit with 30k live `Variant`s and pending garbage (plain objects only) | no crash, 5/5 runs |

Stack in real Godot (`inventory`):

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

Same exception and the same Godot lines as the report, but the second dispose comes from the owner's finalizer,
not from `Disposer`'s own finalizer as in the report. .NET finalizes the `Disposer` first, then the owner disposes it
again. The reporter's exact frame order has not been reproduced in real Godot.

Note: `GodotObject.Dispose(bool)` already guards against running twice (`if (_disposed) return;`);
`Variant.Disposer` does not (nor, at a glance, `Array`, `Dictionary`, `StringName`, `NodePath`).

## Standalone harness (copied Godot types)

The rest of this README is about `src/`, a plain .NET harness used first to understand the mechanism.

## What the code shows

`src/GodotCopy.cs` is a faithful copy of `DisposablesTracker` and the `Variant.Disposer` from master (identical in
4.7.2-stable; verbatim excerpts in `upstream-*.cs.txt`). Only the native `godot_variant` is replaced by a fake that
counts frees.

- `Disposer.Dispose(bool)` is not idempotent. A second run throws `Disposable not registered` and also frees the
  native variant a second time (a double free in the engine).
- `UnregisterDisposable` is the only code that removes the entry, and an explicit `Dispose()` calls
  `GC.SuppressFinalize`. So the reported stack needs two threads inside `Dispose(bool)` on the same `Disposer` at
  once: the finalizer thread already running `~Disposer`, and another thread disposing it after it became
  unreachable. An unhandled exception on the finalizer thread terminates the process.

## Reproduction (macOS arm64, .NET 10; the reporter used .NET 9)

```bash
dotnet build src -c Release -o build
dotnet build/repro.dll double-dispose   # reporter's snippet: catchable exception, native freed twice
dotnet build/repro.dll finalizer-race   # staged: exact reported stack, exit 134 (SIGABRT)
dotnet build/repro.dll race-loop 30     # unstaged GC loop: same crash within the first batch, exit 134
```

`finalizer-race` pauses the finalizer inside the native free, disposes the same `Disposer` from the main thread, then
lets the finalizer continue. `race-loop` has no pauses, only a ~microsecond native free, and crashed within the first
batch on every run.

## Not established yet

The harness reaches a dying `Disposer` through a long weak reference (`trackResurrection: true`). That stands in for
whatever lets engine code reach a `Variant` copy after its `Disposer` became unreachable (for example a native GC
handle). The real trigger in the reporter's game is unknown; their own snippet only shows the non-idempotent dispose.
The crash site, exception and abort match the report.
