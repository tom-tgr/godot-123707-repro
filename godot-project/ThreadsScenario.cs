using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

// Scenario "threads": background workers parse JSON into Variants (e.g. a save game or network messages) and
// hand results to the main thread with CallDeferred; the main thread keeps a short history and drops old ones.
internal sealed class ThreadsScenario : IScenario
{
    private readonly Main _main;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<Variant> _recent = new();
    private long _received;
    private long _parsed;

    public ThreadsScenario(Main main)
    {
        _main = main;
        for (var w = 0; w < 4; w++)
        {
            Task.Run(Worker);
        }
    }

    private void Worker()
    {
        while (!_stop.IsCancellationRequested)
        {
            var parsed = Json.ParseString(GameData.ItemJson);
            var dict = parsed.AsGodotDictionary();
            Variant name = dict["name"];
            _main.CallDeferred(Main.MethodName.Deliver, name);
            Interlocked.Increment(ref _parsed);
            GameData.Churn();
        }
    }

    public void Receive(Variant value)
    {
        _recent.Enqueue(value);
        if (_recent.Count > 64)
            _recent.Dequeue(); // dropped, left to the finalizer
        _received++;
    }

    public void Frame() => GameData.Churn();

    public string Summary()
    {
        _stop.Cancel();
        return $"{Interlocked.Read(ref _parsed)} parsed on workers, {_received} delivered to the main thread";
    }
}
