namespace Lofrano.BoundedObservedSet;

internal sealed record Fact(string Element, bool Removed);

internal sealed class ReplicaState
{
    internal string[] Members { get; }
    internal Dictionary<string, long> Counters { get; }
    internal Dictionary<Dot, Fact> Facts { get; } = new();

    internal ReplicaState(string[] members)
    {
        Members = (string[])members.Clone();
        Counters = members.ToDictionary(member => member, _ => 0L, StringComparer.Ordinal);
    }

    internal ReplicaState Copy()
    {
        var copy = new ReplicaState(Members);
        foreach (var entry in Counters)
            copy.Counters[entry.Key] = entry.Value;
        foreach (var entry in Facts)
            copy.Facts.Add(entry.Key, entry.Value);
        return copy;
    }
}
