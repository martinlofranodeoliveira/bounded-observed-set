using Lofrano.BoundedObservedSet;
using Xunit;
using static BoundedObservedSet.Tests.Fixtures;

namespace BoundedObservedSet.Tests;

public sealed class MergeLawsTests
{
    private static ObservedSet[] Histories()
    {
        var a = New();
        var b = New("B");
        var c = New("C");
        Ok(a.Add("x"));
        Ok(b.Merge(a));
        Ok(b.RemoveObserved("x"));
        Ok(b.Add("y"));
        Ok(a.Add("x"));
        Ok(c.Add("z"));
        Ok(c.RemoveObserved("z"));
        return new[] { a, b, c };
    }

    [Fact]
    public void SixOrdersRepeatAndSelfMergeHaveIdenticalBytes()
    {
        ObservedSet[] states = Histories();
        int[][] orders =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };
        byte[]? expected = null;
        foreach (int[] order in orders)
        {
            var combined = New("C");
            foreach (int index in order)
                Ok(combined.Merge(states[index]));
            expected ??= combined.ExportSnapshot();
            Assert.Equal(expected, combined.ExportSnapshot());
            foreach (ObservedSet source in states)
                Ok(combined.Merge(source));
            Ok(combined.Merge(combined));
            Assert.Equal(expected, combined.ExportSnapshot());
            Assert.Equal(new[] { "x", "y" }, combined.GetVisibleElements());
            Assert.Equal(new[] { "A:1:x:True", "A:2:x:False", "B:1:y:False", "C:1:z:True" }, Rows(combined));
        }
    }

    [Fact]
    public void AcceptedJoinIsCommutativeAndAssociative()
    {
        ObservedSet[] states = Histories();
        for (int i = 0; i < states.Length; i++)
        for (int j = 0; j < states.Length; j++)
        {
            var left = Copy(states[i]);
            var right = Copy(states[j]);
            Ok(left.Merge(states[j]));
            Ok(right.Merge(states[i]));
            Assert.Equal(left.ExportSnapshot(), right.ExportSnapshot());
        }
        var ab = Copy(states[0]);
        Ok(ab.Merge(states[1]));
        Ok(ab.Merge(states[2]));
        var bc = Copy(states[1]);
        Ok(bc.Merge(states[2]));
        var aBc = Copy(states[0]);
        Ok(aBc.Merge(bc));
        Assert.Equal(ab.ExportSnapshot(), aBc.ExportSnapshot());
    }

    [Fact]
    public void ConflictingDotRejectsEvenWithOtherNewFacts()
    {
        var a = New();
        Ok(a.Add("x"));
        var hostile = New("B");
        Ok(hostile.ImportSnapshot(Wire(a: "1", b: "1", facts: "[" +
            FactJson("B", 1, "new") + "," + FactJson("A", 1, "different") + "]")));
        byte[] before = a.ExportSnapshot();
        Assert.Equal(ResultCode.DotConflict, a.Merge(hostile).Code);
        Assert.Equal(before, a.ExportSnapshot());
        Assert.Equal(new long[] { 1, 0, 0 }, Counters(a));
    }

    [Fact]
    public void MembershipIsASetButMustMatchExactly()
    {
        var a = New();
        var reordered = new ObservedSet("B", new[] { "C", "A", "B" });
        Assert.Equal(a.ExportSnapshot(), reordered.ExportSnapshot());
        var smaller = new ObservedSet("A", new[] { "A", "B" });
        byte[] before = a.ExportSnapshot();
        Assert.Equal(ResultCode.IncompatibleMembership, a.Merge(smaller).Code);
        Assert.Equal(ResultCode.IncompatibleMembership, a.ImportSnapshot(smaller.ExportSnapshot()).Code);
        Assert.Equal(before, a.ExportSnapshot());
    }
}
