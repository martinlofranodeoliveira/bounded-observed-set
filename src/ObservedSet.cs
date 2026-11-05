namespace Lofrano.BoundedObservedSet;

public sealed class ObservedSet
{
    public const int MaxSnapshotBytes = SnapshotCodec.ByteLimit;
    private readonly object gate = new();
    private readonly string writer;
    private readonly int historyLimit;
    private readonly int elementLimit;
    private ReplicaState state;

    public ObservedSet(string localReplica, IReadOnlyList<string> members, SetLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count is < 1 or > 16)
            throw new ArgumentException("Membership must contain one to sixteen replica IDs.", nameof(members));
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < members.Count; i++)
        {
            string member = members[i];
            if (!TextRules.IsReplica(member) || !names.Add(member))
                throw new ArgumentException("Membership contains an invalid or duplicate replica ID.", nameof(members));
        }
        if (!TextRules.IsReplica(localReplica) || !names.Contains(localReplica))
            throw new ArgumentException("The local writer must be a member.", nameof(localReplica));
        limits ??= new SetLimits();
        if (limits.HistoryDots is < 1 or > 4096 || limits.DistinctElements is < 1 or > 1024)
            throw new ArgumentException("Limits exceed the supported range.", nameof(limits));
        writer = localReplica;
        historyLimit = limits.HistoryDots;
        elementLimit = limits.DistinctElements;
        state = new ReplicaState(names.Order(StringComparer.Ordinal).ToArray());
    }

    public OperationResult Add(string? element)
    {
        if (!TextRules.IsElement(element))
            return new(ResultCode.InvalidElement);
        lock (gate)
        {
            if (state.Facts.Count == historyLimit)
                return new(ResultCode.HistoryLimit);
            bool known = state.Facts.Values.Any(f => StringComparer.Ordinal.Equals(f.Element, element));
            if (!known && state.Facts.Values.Select(f => f.Element).Distinct(StringComparer.Ordinal).Count() == elementLimit)
                return new(ResultCode.ElementLimit);
            if (state.Counters[writer] == long.MaxValue)
                return new(ResultCode.CounterOverflow);
            long next = checked(state.Counters[writer] + 1);
            var dot = new Dot(writer, next);
            state.Facts.Add(dot, new Fact(element!, false));
            state.Counters[writer] = next;
            return new(ResultCode.Success, dot);
        }
    }

    public OperationResult RemoveObserved(string? element)
    {
        if (!TextRules.IsElement(element))
            return new(ResultCode.InvalidElement);
        lock (gate)
        {
            Dot[] targets = state.Facts.Where(p => !p.Value.Removed &&
                    StringComparer.Ordinal.Equals(p.Value.Element, element))
                .Select(p => p.Key).ToArray();
            foreach (Dot dot in targets)
                state.Facts[dot] = state.Facts[dot] with { Removed = true };
            return new(ResultCode.Success);
        }
    }

    public bool Contains(string? element)
    {
        if (!TextRules.IsElement(element))
            return false;
        lock (gate)
            return state.Facts.Values.Any(f => !f.Removed && StringComparer.Ordinal.Equals(f.Element, element));
    }

    public string[] GetVisibleElements()
    {
        lock (gate)
            return state.Facts.Values.Where(f => !f.Removed).Select(f => f.Element)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public OperationResult Merge(ObservedSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        // Capture and release the source lock BEFORE acquiring this instance's lock.
        // Opposite-direction merges never hold two locks at once.
        ReplicaState incoming = other.CopyState();
        lock (gate)
        {
            if (!state.Members.SequenceEqual(incoming.Members, StringComparer.Ordinal))
                return new(ResultCode.IncompatibleMembership);
            ReplicaState candidate = state.Copy();
            foreach (var entry in incoming.Facts)
            {
                if (candidate.Facts.TryGetValue(entry.Key, out Fact? existing))
                {
                    if (!StringComparer.Ordinal.Equals(existing.Element, entry.Value.Element))
                        return new(ResultCode.DotConflict);
                    candidate.Facts[entry.Key] = existing with { Removed = existing.Removed || entry.Value.Removed };
                }
                else
                    candidate.Facts.Add(entry.Key, entry.Value);
            }
            foreach (string member in candidate.Members)
                candidate.Counters[member] = Math.Max(candidate.Counters[member], incoming.Counters[member]);
            ResultCode result = CheckLimits(candidate);
            if (result != ResultCode.Success)
                return new(result);
            state = candidate;
            return new(ResultCode.Success);
        }
    }

    public OperationResult ImportSnapshot(ReadOnlySpan<byte> utf8Json)
    {
        ResultCode parsed = SnapshotCodec.Read(utf8Json, out ReplicaState? candidate);
        if (parsed != ResultCode.Success)
            return new(parsed);
        lock (gate)
        {
            if (!state.Members.SequenceEqual(candidate!.Members, StringComparer.Ordinal))
                return new(ResultCode.IncompatibleMembership);
            ResultCode result = CheckLimits(candidate);
            if (result != ResultCode.Success)
                return new(result);
            state = candidate;
            return new(ResultCode.Success);
        }
    }

    public byte[] ExportSnapshot()
    {
        lock (gate)
            return SnapshotCodec.Write(state);
    }

    private ReplicaState CopyState()
    {
        lock (gate)
            return state.Copy();
    }

    private ResultCode CheckLimits(ReplicaState candidate)
    {
        if (candidate.Facts.Count > historyLimit)
            return ResultCode.HistoryLimit;
        if (candidate.Facts.Values.Select(f => f.Element).Distinct(StringComparer.Ordinal).Count() > elementLimit)
            return ResultCode.ElementLimit;
        return ResultCode.Success;
    }
}
