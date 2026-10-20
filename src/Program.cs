using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;

// Repro harness for godotengine/godot#123707. Modes:
//   double-dispose   the reporter's snippet: two Variant copies disposed -> catchable ArgumentException
//   finalizer-race   staged: the finalizer thread is paused inside Disposer.Dispose(false) while another thread
//                    disposes the same Disposer -> unhandled ArgumentException on the finalizer thread -> abort
//   race-loop        unstaged: a tiny delay in the native free, a GC loop, and a thread disposing objects the
//                    finalizer may be working on at the same time; runs until the process aborts or time is up
// The long weak reference (trackResurrection: true) stands in for whatever lets engine code reach a Disposer
// after it became unreachable (native GC handles); the production trigger is not known yet.
return (args.Length > 0 ? args[0] : "") switch
{
    "double-dispose" => DoubleDispose(),
    "finalizer-race" => FinalizerRace(),
    "race-loop" => RaceLoop(seconds: args.Length > 1 ? int.Parse(args[1]) : 30),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: repro double-dispose | finalizer-race | race-loop [seconds]");
    return 1;
}

static int DoubleDispose()
{
    Variant variant = Variant.CreateHeapValue("test");
    var copy = variant;
    variant.Dispose();
    try
    {
        copy.Dispose();
        Console.WriteLine("second Dispose() did not throw");
    }
    catch (ArgumentException e)
    {
        Console.WriteLine($"second Dispose() threw on the calling thread: {e.Message}");
    }

    Console.WriteLine($"native value freed {FakeNativeVariant.FreeCount(copy.NativeVar.Id)} time(s)");
    return 0;
}

static int FinalizerRace()
{
    using var inFinalizer = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();

    // The first free of our value comes from the finalizer: pause it there, before UnregisterDisposable.
    var (weak, id) = CreateAndDrop();
    FakeNativeVariant.OnFree = freedId =>
    {
        if (freedId == id && FakeNativeVariant.FreeCount(freedId) == 1)
        {
            inFinalizer.Set();
            release.Wait();
        }
    };

    GC.Collect(); // the Disposer is unreachable now: it goes on the finalization queue
    if (!inFinalizer.Wait(TimeSpan.FromSeconds(10)))
    {
        Console.WriteLine("finalizer never ran; nothing reproduced");
        return 2;
    }

    Console.WriteLine("1. finalizer thread is inside Disposer.Dispose(false), before UnregisterDisposable");

    if (!weak.TryGetTarget(out var disposer))
    {
        Console.WriteLine("could not reach the Disposer any more; nothing reproduced");
        return 2;
    }

    disposer.Dispose(); // what Variant.Dispose() on a copy does: unregisters, then SuppressFinalize (too late)
    Console.WriteLine("2. another thread disposed the same Disposer: unregistered it, SuppressFinalize came too late");
    Console.WriteLine($"3. native value freed {FakeNativeVariant.FreeCount(id)} times (2 = double free in the engine)");
    Console.WriteLine("4. letting the finalizer continue to UnregisterDisposable...");
    Console.Out.Flush();

    release.Set();
    Thread.Sleep(3000);
    Console.WriteLine("process is still alive: not reproduced");
    return 0;
}

static int RaceLoop(int seconds)
{
    // ~tens of microseconds of native work in every free widens the window, as a real native free would.
    FakeNativeVariant.OnFree = _ => Thread.SpinWait(500);

    var stopwatch = Stopwatch.StartNew();
    long rounds = 0, lostRaceOnMainThread = 0, disposedFromMainThread = 0;
    while (stopwatch.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        var batch = new WeakReference<IDisposable>[256];
        for (var i = 0; i < batch.Length; i++)
        {
            batch[i] = CreateAndDrop().Weak;
        }

        GC.Collect(); // queue the whole batch for finalization; the finalizer thread starts working through it
        foreach (var weak in batch)
        {
            if (!weak.TryGetTarget(out var disposer))
                continue;

            try
            {
                disposer.Dispose();
                disposedFromMainThread++;
            }
            catch (ArgumentException)
            {
                lostRaceOnMainThread++; // the finalizer unregistered first; the main thread can catch this one
            }
        }

        rounds++;
    }

    Console.WriteLine($"no crash in {seconds}s: {rounds} rounds, {disposedFromMainThread} disposed from the main thread, " +
                      $"{lostRaceOnMainThread} caught ArgumentExceptions on the main thread");
    return 0;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (WeakReference<IDisposable> Weak, int Id) CreateAndDrop()
{
    var variant = Variant.CreateHeapValue("test");
    return (new WeakReference<IDisposable>(variant.DisposerForRepro!, trackResurrection: true), variant.NativeVar.Id);
}
