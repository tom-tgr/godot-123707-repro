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
