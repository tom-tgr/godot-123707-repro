using Godot;

// Runs one scenario for N seconds of real frames, then quits. Nothing here touches Godot internals or forces GC
// timing: garbage collections happen from ordinary allocation pressure, like in a game.
//   Godot --headless --path godot-project -- <scenario> [seconds]
// Scenarios: inventory | inventory-careful | plain-fields | threads | quit | native-held
public partial class Main : Node
{
    private IScenario _scenario = null!;
    private double _seconds;
    private double _elapsed;
    private int _frames;
    private int _gen0AtStart;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        var name = args.Length > 0 ? args[0] : "inventory";
        _seconds = args.Length > 1 ? double.Parse(args[1]) : 20;
        _scenario = name switch
        {
            "inventory" => new InventoryScenario(careful: false),
            "inventory-careful" => new InventoryScenario(careful: true),
            "plain-fields" => new PlainFieldsScenario(),
            "threads" => new ThreadsScenario(this),
            "quit" => new QuitScenario(),
            "native-held" => new NativeHeldScenario(),
            _ => throw new System.ArgumentException($"unknown scenario '{name}'"),
        };
        _gen0AtStart = System.GC.CollectionCount(0);
        GD.Print($"scenario={name} seconds={_seconds} godot={Engine.GetVersionInfo()["string"]} dotnet={System.Environment.Version}");
    }

    public override void _Process(double delta)
    {
        _scenario.Frame();
        _frames++;
        _elapsed += delta;
        if (_elapsed < _seconds && !_scenario.WantsQuit)
            return;

        GD.Print($"done: {_frames} frames, {System.GC.CollectionCount(0) - _gen0AtStart} gen0 GCs, {_scenario.Summary()}");
        GetTree().Quit();
    }

    public void Deliver(Variant value) => (_scenario as ThreadsScenario)?.Receive(value);
}

internal interface IScenario
{
    bool WantsQuit => false;
    void Frame();
    string Summary();
}

internal static class GameData
{
    // An item record as a game would load it from a JSON file.
    public const string ItemJson =
        "{\"name\":\"Sword of a Thousand Truths\",\"stats\":{\"attack\":12,\"weight\":3.5,\"tags\":[\"rare\",\"melee\"]}," +
        "\"description\":\"A blade forged in the depths of a very long quest log.\"}";

    // Everyday garbage: strings, lists, buffers. This is what makes the GC run, not GC.Collect().
    public static int Churn()
    {
        var total = 0;
        for (var i = 0; i < 200; i++)
        {
            var buffer = new byte[2048];
            var label = $"enemy-{i}-{buffer.Length}";
            total += label.Length;
        }

        return total;
    }
}
