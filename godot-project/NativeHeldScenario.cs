using Godot;

// Scenario "native-held": objects with Variant fields (no Dispose override, no user double dispose) are kept alive
// by the engine rather than by C#: in a Godot Array, as node metadata, and passed through a signal. C# drops its
// references, the engine releases them a frame later. This exercises the weak/strong GC handle swap the engine
// does when a RefCounted's native refcount changes (csharp_script.cpp), the only engine path that can hand a
// dying managed object back to the main thread.
public partial class Loot : RefCounted
{
    public Variant Name;
    public Variant Stats;
}

public partial class LootBus : Node
{
    [Signal]
    public delegate void DroppedEventHandler(Loot loot);
}

internal sealed class NativeHeldScenario : IScenario
{
    private readonly Godot.Collections.Dictionary _record = Json.ParseString(GameData.ItemJson).AsGodotDictionary();
    private readonly Godot.Collections.Array _heldByEngine = new();
    private readonly Node _metaHolder = new();
    private readonly LootBus _bus = new();
    private Loot? _lastSeenBySignal;
    private long _created;
    private int _frame;

    public NativeHeldScenario()
    {
        _bus.Dropped += loot => _lastSeenBySignal = loot; // C# keeps only the latest one
    }

    public void Frame()
    {
        // Release last frame's objects on the engine side: refcounts drop, handles swap back to weak.
        _heldByEngine.Clear();
        _metaHolder.RemoveMeta("loot");

        for (var i = 0; i < 300; i++)
        {
            var loot = new Loot { Name = _record["name"], Stats = _record["stats"] };
            _heldByEngine.Add(loot);             // engine holds it: refcount 2, strong handle
            if (i == 0)
                _metaHolder.SetMeta("loot", loot);
            if (i % 10 == 0)
                _bus.EmitSignal(LootBus.SignalName.Dropped, loot);
            _created++;
        } // C# references go out of scope here

        GameData.Churn();
        _frame++;
    }

    public string Summary() => $"{_created} engine-held loot objects created over {_frame} frames";
}
