using System.Collections.Generic;
using Godot;

// Scenario "quit": the player quits while lots of Variants are alive or already garbage, so Godot's shutdown
// (DisposablesTracker disposing every tracked instance) overlaps with the finalizer thread. Uses only plain
// objects (no Dispose override) so a crash here would come from shutdown itself.
internal sealed class QuitScenario : IScenario
{
    private readonly Godot.Collections.Dictionary _record = Json.ParseString(GameData.ItemJson).AsGodotDictionary();
    private readonly List<Variant> _alive = new();
    private int _frames;

    public bool WantsQuit => _frames >= 30;

    public void Frame()
    {
        for (var i = 0; i < 2000; i++)
        {
            Variant value = _record["description"];
            if (i % 2 == 0)
                _alive.Add(value); // still referenced at quit
            _ = new Pickup { Label = _record["name"], Payload = _record["stats"] }; // garbage, waiting for the finalizer
        }

        GameData.Churn();
        _frames++;
    }

    public string Summary() => $"{_alive.Count} Variants alive at quit, quitting now";
}
