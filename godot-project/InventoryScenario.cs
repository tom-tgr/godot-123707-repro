using Godot;

// Scenario "inventory": item objects keep Variants read from parsed JSON and dispose them in a Dispose(bool)
// override, a common way to "clean up Variants". Items are dropped without calling Dispose(), so the
// GodotObject finalizer runs Dispose(false), which disposes the fields on the finalizer thread.
public partial class ItemData : RefCounted
{
    private Variant _name;
    private Variant _stats;

    public ItemData()
    {
    }

    public ItemData(Variant name, Variant stats)
    {
        _name = name;
        _stats = stats;
    }

    protected override void Dispose(bool disposing)
    {
        _name.Dispose();
        _stats.Dispose();
        base.Dispose(disposing);
    }
}

// The textbook version: only dispose other objects when disposing == true (never from the finalizer).
public partial class CarefulItemData : RefCounted
{
    private Variant _name;
    private Variant _stats;

    public CarefulItemData()
    {
    }

    public CarefulItemData(Variant name, Variant stats)
    {
        _name = name;
        _stats = stats;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _name.Dispose();
            _stats.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class InventoryScenario(bool careful) : IScenario
{
    private readonly Godot.Collections.Dictionary _record = Json.ParseString(GameData.ItemJson).AsGodotDictionary();
    private long _items;

    public void Frame()
    {
        // Open a chest: build a few hundred items from the record, show them for a frame, drop them.
        for (var i = 0; i < 500; i++)
        {
            if (careful)
                _ = new CarefulItemData(_record["name"], _record["stats"]);
            else
                _ = new ItemData(_record["name"], _record["stats"]);
            _items++;
        }

        GameData.Churn();
    }

    public string Summary() => $"{_items} {(careful ? "careful " : "")}items created";
}
