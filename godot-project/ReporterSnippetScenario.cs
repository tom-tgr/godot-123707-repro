using System.Threading;
using System.Threading.Tasks;
using Godot;

// Scenario "snippet-<where>": the reporter's own steps to reproduce, verbatim, run in real Godot:
//     Variant variant = "test";
//     var copy = variant;
//     variant.Dispose();
//     copy.Dispose();   // ArgumentException: Disposable not registered.
// <where> = main (inside a Godot callback), task (Task.Run worker) or thread (a plain System.Threading.Thread).
internal sealed class ReporterSnippetScenario(string where) : IScenario
{
    private bool _ran;
    private int _frames;

    public bool WantsQuit => _frames >= 60; // give the engine a second to report anything after the snippet

    public static void Snippet()
    {
        Variant variant = "test";
        var copy = variant;
        variant.Dispose();
        copy.Dispose();   // ArgumentException: Disposable not registered.
    }

    public void Frame()
    {
        _frames++;
        if (_ran)
            return;

        _ran = true;
        GD.Print($"running the reporter's snippet on: {where}");
        switch (where)
        {
            case "main":
                Snippet();
                break;
            case "task":
                Task.Run(Snippet);
                break;
            case "thread":
                new Thread(Snippet).Start();
                break;
        }

        GD.Print("snippet call returned to the frame loop");
    }

    public string Summary() => $"snippet ran on {where}; the game kept running for {_frames} frames";
}
