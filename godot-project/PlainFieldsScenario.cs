using Godot;

// Scenario "plain-fields": the most common code. Objects hold Variants from Godot APIs (dictionary lookups,
// metadata, method results) and are simply dropped; nobody disposes anything, finalizers clean up.
public partial class Pickup : RefCounted
{
    public Variant Label;
    public Variant Payload;
    public Variant Extra;
}

internal sealed class PlainFieldsScenario : IScenario
{
    private readonly Godot.Collections.Dictionary _record = Json.ParseString(GameData.ItemJson).AsGodotDictionary();
    private readonly Node _carrier = new();
    private long _pickups;

    public PlainFieldsScenario() => _carrier.SetMeta("loot_table", Json.ParseString(GameData.ItemJson));

    public void Frame()
    {
        for (var i = 0; i < 500; i++)
        {
            var pickup = new Pickup
            {
                Label = _record["description"],
                Payload = _carrier.GetMeta("loot_table"),
                Extra = _carrier.Call(Node.MethodName.GetName),
            };
            Variant copy = pickup.Label; // struct copies share one native value
            _ = copy;
            _pickups++;
        }

        GameData.Churn();
    }

    public string Summary() => $"{_pickups} pickups created";
}
