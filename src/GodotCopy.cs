// Faithful copies of the two Godot types involved in godotengine/godot#123707, taken from master
// (identical in 4.7.2-stable): modules/mono/glue/GodotSharp/GodotSharp/Core/DisposablesTracker.cs and
// the Disposer nested in Core/Variant.cs. Verbatim excerpts are kept in ../upstream-*.cs.txt.
//
// Only differences from upstream:
// - the native godot_variant is replaced by FakeNativeVariant, which counts how often it is freed;
// - DisposablesTracker's shutdown hook (needs the engine) is left out; Register/Unregister are verbatim;
// - Variant is reduced to the parts that matter: a heap-type value gets a Disposer, Dispose() forwards to it,
//   and DisposerForRepro exposes the Disposer so the harness can reach it the way engine code would.

using System.Collections.Concurrent;

namespace Godot;

#nullable enable

internal static class DisposablesTracker
{
    private static ConcurrentDictionary<WeakReference<IDisposable>, byte> OtherInstances { get; } =
        new();

    public static WeakReference<IDisposable> RegisterDisposable(IDisposable disposable)
    {
        var weakReferenceToSelf = new WeakReference<IDisposable>(disposable);
        OtherInstances.TryAdd(weakReferenceToSelf, 0);
        return weakReferenceToSelf;
    }

    public static void UnregisterDisposable(WeakReference<IDisposable> weakReference)
    {
        if (!OtherInstances.TryRemove(weakReference, out _))
            throw new ArgumentException("Disposable not registered.", nameof(weakReference));
    }
}

public partial struct Variant : IDisposable
{
    internal FakeNativeVariant NativeVar;
#pragma warning disable CS0414 // kept for fidelity with upstream; unused in the harness
    private object? _obj;
#pragma warning restore CS0414
    private Disposer? _disposer;

    private sealed class Disposer : IDisposable
    {
        private FakeNativeVariant _native;

        private WeakReference<IDisposable>? _weakReferenceToSelf;

        public Disposer(in FakeNativeVariant nativeVar)
        {
            _native = nativeVar;
            _weakReferenceToSelf = DisposablesTracker.RegisterDisposable(this);
        }

        ~Disposer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            _native.Dispose();

            if (_weakReferenceToSelf != null)
            {
                DisposablesTracker.UnregisterDisposable(_weakReferenceToSelf);
            }
        }
    }

    // A heap-backed value (String, Array, Object, ...): upstream gives these a Disposer.
    public static Variant CreateHeapValue(string value)
    {
        var native = FakeNativeVariant.Create();
        return new Variant { NativeVar = native, _obj = value, _disposer = new Disposer(native) };
    }

    public void Dispose()
    {
        _disposer?.Dispose();
        NativeVar = default;
        _obj = null;
    }

    internal IDisposable? DisposerForRepro => _disposer;
}
