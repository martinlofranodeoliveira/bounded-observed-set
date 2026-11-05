using Lofrano.BoundedObservedSet;
using Xunit;
using static BoundedObservedSet.Tests.Fixtures;

namespace BoundedObservedSet.Tests;

public sealed class LimitsAndConcurrencyTests
{
    [Fact]
    public void UnionExhaustionIsAtomicIncludingCounters()
    {
        var a = New(limits: new SetLimits(2, 2));
        var b = New("B");
        Ok(a.Add("x"));
        Ok(b.Add("y"));
        Ok(b.Add("z"));
        byte[] before = a.ExportSnapshot();
        Assert.Equal(ResultCode.HistoryLimit, a.Merge(b).Code);
        Assert.Equal(before, a.ExportSnapshot());
        Assert.Equal(new Dot("A", 2), a.Add("x").Dot);
        byte[] full = a.ExportSnapshot();
        Assert.Equal(ResultCode.HistoryLimit, a.Add("x").Code);
        Assert.Equal(full, a.ExportSnapshot());
    }

    [Fact]
    public void RemovedElementsStillConsumeDistinctElementBudget()
    {
        var a = New(limits: new SetLimits(4, 1));
        Ok(a.Add("x"));
        Ok(a.RemoveObserved("x"));
        byte[] before = a.ExportSnapshot();
        Assert.Equal(ResultCode.ElementLimit, a.Add("y").Code);
        Assert.Equal(before, a.ExportSnapshot());
        Ok(a.Add("x"));
        var b = New("B");
        Ok(b.Add("y"));
        before = a.ExportSnapshot();
        Assert.Equal(ResultCode.ElementLimit, a.Merge(b).Code);
        Assert.Equal(before, a.ExportSnapshot());
        Assert.Equal(ResultCode.ElementLimit, a.ImportSnapshot(Wire(a: "1", b: "1", facts: "[" + FactJson("A", 1, "x", true) + "," + FactJson("B", 1, "y") + "]")).Code);
        Assert.Equal(before, a.ExportSnapshot());
    }

    [Fact]
    public void ImportRespectsLocalCapAndOverflowNeverMutates()
    {
        var small = New(limits: new SetLimits(1, 1));
        byte[] before = small.ExportSnapshot();
        Assert.Equal(ResultCode.HistoryLimit, small.ImportSnapshot(Wire(a: "2", facts: "[" + FactJson("A", 1, "x") + "," + FactJson("A", 2, "x") + "]")).Code);
        Assert.Equal(before, small.ExportSnapshot());
        Ok(small.ImportSnapshot(Wire(a: "9223372036854775807")));
        before = small.ExportSnapshot();
        Assert.Equal(ResultCode.CounterOverflow, small.Add("x").Code);
        Assert.Equal(before, small.ExportSnapshot());
    }

    [Fact]
    public void FailureDiagnosticPrecedenceIsDeterministicAndAtomic()
    {
        var full = New(limits: new SetLimits(1, 1));
        Ok(full.ImportSnapshot(Wire(a: "9223372036854775807", facts: "[" + FactJson("A", 9223372036854775807, "x") + "]")));
        byte[] before = full.ExportSnapshot();
        Assert.Equal(ResultCode.InvalidElement, full.Add(null).Code);
        Assert.Equal(ResultCode.HistoryLimit, full.Add("y").Code);
        Assert.Equal(before, full.ExportSnapshot());

        var elementCapped = New(limits: new SetLimits(2, 1));
        Ok(elementCapped.ImportSnapshot(Wire(a: "9223372036854775807", facts: "[" + FactJson("A", 9223372036854775807, "x") + "]")));
        before = elementCapped.ExportSnapshot();
        Assert.Equal(ResultCode.ElementLimit, elementCapped.Add("y").Code);
        Assert.Equal(ResultCode.CounterOverflow, elementCapped.Add("x").Code);
        Assert.Equal(before, elementCapped.ExportSnapshot());

        var target = New(limits: new SetLimits(1, 1));
        Ok(target.Add("x"));
        before = target.ExportSnapshot();
        var source = New("B");
        Ok(source.ImportSnapshot(Wire(a: "1", b: "2", facts: "[" +
            FactJson("B", 1, "y") + "," +
            FactJson("B", 2, "z") + "," +
            FactJson("A", 1, "conflict") + "]")));
        Assert.Equal(ResultCode.DotConflict, target.Merge(source).Code);
        Assert.Equal(before, target.ExportSnapshot());

        var bothLimits = New("B");
        Ok(bothLimits.Add("y"));
        Assert.Equal(ResultCode.HistoryLimit, target.Merge(bothLimits).Code);
        Assert.Equal(before, target.ExportSnapshot());
    }

    [Fact]
    public void ConstructorRejectsUnsupportedConfigurations()
    {
        Assert.Throws<ArgumentNullException>(() => new ObservedSet("A", null!));
        Assert.Throws<ArgumentException>(() => new ObservedSet(null!, new[] { "A" }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", new string[] { "A", null! }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", new[] { "A", "A" }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", new[] { "A", "bad id" }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("Z", new[] { "A" }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", new[] { "A", new string('b', 33) }));
        Assert.Throws<ArgumentException>(() => new ObservedSet("A", Enumerable.Range(0, 17).Select(i => "A" + i).ToArray()));
        Assert.Throws<ArgumentException>(() => New(limits: new SetLimits(0, 1)));
        Assert.Throws<ArgumentException>(() => New(limits: new SetLimits(1, 0)));
        Assert.Throws<ArgumentException>(() => New(limits: new SetLimits(4097, 1)));
        Assert.Throws<ArgumentException>(() => New(limits: new SetLimits(1, 1025)));
        string[] mutableMembers = { "B", "A" };
        var one = new ObservedSet("A", mutableMembers);
        mutableMembers[0] = "Z";
        Assert.Throws<ArgumentNullException>(() => one.Merge(null!));
        Ok(one.Add("x"));
    }

    [Fact]
    public void ConcurrentAddsUseDistinctCheckedDots()
    {
        var set = New();
        Parallel.For(0, 1000, _ => Ok(set.Add("x")));
        Assert.Equal(1000, Rows(set).Length);
        Assert.Equal(new long[] { 1000, 0, 0 }, Counters(set));
        Assert.Equal(new Dot("A", 1001), set.Add("x").Dot);
    }

    [Fact]
    public async Task OppositeDirectionMergesDoNotNestLocks()
    {
        var a = New();
        var b = New("B");
        Ok(a.Add("x"));
        Ok(b.Add("y"));
        await Task.WhenAll(
            Task.Run(() => { for (int i = 0; i < 200; i++) Ok(a.Merge(b)); }, TestContext.Current.CancellationToken),
            Task.Run(() => { for (int i = 0; i < 200; i++) Ok(b.Merge(a)); }, TestContext.Current.CancellationToken)
        ).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Ok(a.Merge(b));
        Ok(b.Merge(a));
        Assert.Equal(a.ExportSnapshot(), b.ExportSnapshot());
        Assert.Equal(new[] { "x", "y" }, a.GetVisibleElements());
    }
}
