using Lofrano.BoundedObservedSet;
using Xunit;
using static BoundedObservedSet.Tests.Fixtures;

namespace BoundedObservedSet.Tests;

public sealed class HistoryOracleTests
{
    // Independent event-history model: replicas receive add events and removal
    // events carrying observed add-event IDs. It has no dot->removed map and does
    // not invoke the production merger to compute expectations.
    private sealed record Event(int Id, int Writer, long Sequence, string Element, int[]? Targets);

    private sealed class Oracle
    {
        internal readonly List<Event> Events = new();
        internal readonly HashSet<int>[] Known = { new(), new(), new() };
        private readonly long[] issued = new long[3];

        internal void Add(int replica, string element)
        {
            int id = Events.Count;
            Events.Add(new Event(id, replica, ++issued[replica], element, null));
            Known[replica].Add(id);
        }

        internal void Remove(int replica, string element)
        {
            HashSet<int> removed = Removed(replica);
            int[] targets = Known[replica].Select(id => Events[id])
                .Where(e => e.Targets is null && e.Element == element && !removed.Contains(e.Id))
                .Select(e => e.Id).ToArray();
            int id = Events.Count;
            Events.Add(new Event(id, replica, 0, element, targets));
            Known[replica].Add(id);
        }

        internal void Receive(int target, int source) => Known[target].UnionWith(Known[source]);

        private HashSet<int> Removed(int replica) => Known[replica].Select(id => Events[id])
            .Where(e => e.Targets is not null).SelectMany(e => e.Targets!).ToHashSet();

        internal void Check(int replica, ObservedSet actual)
        {
            HashSet<int> removed = Removed(replica);
            Event[] adds = Known[replica].Select(id => Events[id]).Where(e => e.Targets is null).ToArray();
            string[] visible = adds.Where(e => !removed.Contains(e.Id)).Select(e => e.Element)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(visible, actual.GetVisibleElements());
            Assert.Equal(visible.Contains("x", StringComparer.Ordinal), actual.Contains("x"));
            Assert.Equal(visible.Contains("y", StringComparer.Ordinal), actual.Contains("y"));
            string[] rows = adds.OrderBy(e => e.Writer).ThenBy(e => e.Sequence)
                .Select(e => $"{(char)('A' + e.Writer)}:{e.Sequence}:{e.Element}:{removed.Contains(e.Id)}").ToArray();
            Assert.Equal(rows, Rows(actual));
            long[] counters = Enumerable.Range(0, 3).Select(writer => adds.Where(e => e.Writer == writer)
                .Select(e => e.Sequence).DefaultIfEmpty(0).Max()).ToArray();
            Assert.Equal(counters, Counters(actual));
        }
    }

    [Fact]
    public void EnumerateAllFourStepHistoriesAgainstEventOracle()
    {
        // Eight actions, 8^4 = 4096 deterministic histories, all within caps.
        // Replica writers are authoritative within each separate experiment.
        for (int encoded = 0; encoded < 4096; encoded++)
        {
            var replicas = new[] { New(), New("B"), New("C") };
            var oracle = new Oracle();
            int remaining = encoded;
            for (int step = 0; step < 4; step++)
            {
                int action = remaining % 8;
                remaining /= 8;
                switch (action)
                {
                    case 0: Ok(replicas[0].Add("x")); oracle.Add(0, "x"); break;
                    case 1: Ok(replicas[1].Add("x")); oracle.Add(1, "x"); break;
                    case 2: Ok(replicas[2].Add("y")); oracle.Add(2, "y"); break;
                    case 3: Ok(replicas[0].RemoveObserved("x")); oracle.Remove(0, "x"); break;
                    case 4: Ok(replicas[1].RemoveObserved("x")); oracle.Remove(1, "x"); break;
                    case 5: Ok(replicas[0].Merge(replicas[1])); oracle.Receive(0, 1); break;
                    case 6: Ok(replicas[1].Merge(replicas[2])); oracle.Receive(1, 2); break;
                    case 7: Ok(replicas[2].Merge(replicas[0])); oracle.Receive(2, 0); break;
                }
            }
            for (int replica = 0; replica < 3; replica++)
                oracle.Check(replica, replicas[replica]);
        }
    }
}
