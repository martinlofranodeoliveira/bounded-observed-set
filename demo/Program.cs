using System.Text.Json;
using Lofrano.BoundedObservedSet;

if (args.Any(arg => arg is not ("--self-test" or "--json")))
{
    TryWriteError("Usage: BoundedObservedSet.Demo [--self-test] [--json]");
    return 2;
}

try
{
    string[] members = { "A", "B", "C" };
    var a = new ObservedSet("A", members);
    var b = new ObservedSet("B", members);
    var c = new ObservedSet("C", members);
    Require(a.Add("blue").IsSuccess);
    Require(a.Add("red").IsSuccess);
    Require(b.Merge(a).IsSuccess);
    Require(c.Merge(a).IsSuccess);
    Require(b.RemoveObserved("blue").IsSuccess);
    Require(b.RemoveObserved("red").IsSuccess);
    OperationResult concurrent = a.Add("blue");
    Require(concurrent.IsSuccess && concurrent.Dot == new Dot("A", 3));
    Require(c.Merge(b).IsSuccess);
    Require(!c.Contains("blue") && !c.Contains("red"));
    Require(c.Merge(a).IsSuccess);
    Require(a.Merge(c).IsSuccess);
    Require(b.Merge(c).IsSuccess);
    Require(c.Merge(b).IsSuccess);

    byte[] snapshot = a.ExportSnapshot();
    Require(snapshot.SequenceEqual(b.ExportSnapshot()));
    Require(snapshot.SequenceEqual(c.ExportSnapshot()));
    foreach (ObservedSet replica in new[] { a, b, c })
        Require(replica.GetVisibleElements().SequenceEqual(new[] { "blue" }, StringComparer.Ordinal));
    using var document = JsonDocument.Parse(snapshot);
    JsonElement facts = document.RootElement.GetProperty("facts");
    Require(facts.GetArrayLength() == 3);
    Require(facts.EnumerateArray().Count(f => f.GetProperty("removed").GetBoolean()) == 2);
    Require(facts.EnumerateArray().Single(f => !f.GetProperty("removed").GetBoolean())
        .GetProperty("counter").GetInt64() == 3);

    string jsonLine = JsonSerializer.Serialize(new
    {
        version = "0.1.0",
        replicas = 3,
        converged = true,
        visible = a.GetVisibleElements(),
        historicalDots = facts.GetArrayLength()
    });

    if (!args.Contains("--json", StringComparer.Ordinal))
    {
        Console.Out.WriteLine("Bounded Observed Set - development 0.1.0");
        Console.Out.WriteLine("Observed blue/red were removed; unobserved A:3 survives.");
        Console.Out.WriteLine("Converged: A, B, C; visible: [blue]");
    }
    Console.Out.WriteLine(jsonLine);
    Console.Out.Flush();
    return 0;
}
catch (Exception ex) when (ex is IOException or ObjectDisposedException)
{
    TryWriteError("Demo output failed.");
    return 1;
}
catch (Exception)
{
    TryWriteError("Demo assertion failed.");
    return 1;
}

static void Require(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Demo assertion failed.");
}

static void TryWriteError(string message)
{
    try
    {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
    {
    }
}
