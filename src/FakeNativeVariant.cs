namespace Godot;

// Stands in for the native godot_variant. Each instance has an id; Dispose() records how many times that
// native value was freed (more than once = double free in the real engine) and runs an optional hook so the
// harness can pause a thread inside Disposer.Dispose(bool), before it reaches UnregisterDisposable.
internal struct FakeNativeVariant
{
    private static int s_nextId;
    private static readonly int[] s_freeCounts = new int[1 << 20];

    public static Action<int>? OnFree;

    private int _id;

    public static FakeNativeVariant Create() => new() { _id = Interlocked.Increment(ref s_nextId) };

    public readonly int Id => _id;

    public static int FreeCount(int id) => Volatile.Read(ref s_freeCounts[id]);

    public readonly void Dispose()
    {
        if (_id == 0)
            return; // default value, nothing native behind it

        Interlocked.Increment(ref s_freeCounts[_id]);
        OnFree?.Invoke(_id);
    }
}
