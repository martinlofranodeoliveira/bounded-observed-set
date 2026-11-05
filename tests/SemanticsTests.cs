using Lofrano.BoundedObservedSet;
using Xunit;
using static BoundedObservedSet.Tests.Fixtures;

namespace BoundedObservedSet.Tests;

public sealed class SemanticsTests
{
    [Fact]
    public void ObservedRemovalRetainsEvidence()
    {
        var a = New();
        var b = New("B");
        Ok(a.Add("x"));
        Ok(b.Merge(a));
        Ok(b.RemoveObserved("x"));
        Ok(a.Merge(b));
        Assert.False(a.Contains("x"));
        Assert.Equal(new[] { "A:1:x:True" }, Rows(a));
        Assert.Equal(new long[] { 1, 0, 0 }, Counters(a));
    }

    [Fact]
    public void IndependentConcurrentAddSurvives()
    {
        var a = New();
        var b = New("B");
        Ok(a.Add("x"));
        Ok(b.Add("x"));
        Ok(a.RemoveObserved("x"));
        Ok(a.Merge(b));
        Assert.True(a.Contains("x"));
        Assert.Equal(new[] { "A:1:x:True", "B:1:x:False" }, Rows(a));
    }

    [Fact]
    public void LaterUnobservedDotFromSameWriterSurvives()
    {
        var a = New();
        var b = New("B");
        Ok(a.Add("x"));
        Ok(b.Merge(a));
        Ok(b.RemoveObserved("x"));
        OperationResult next = a.Add("x");
        Ok(next);
        Assert.Equal(new Dot("A", 2), next.Dot);
        Ok(a.Merge(b));
        Assert.Equal(new[] { "A:1:x:True", "A:2:x:False" }, Rows(a));
        Assert.Equal(new[] { "x" }, a.GetVisibleElements());
        Ok(a.RemoveObserved("x"));
        Assert.False(a.Contains("x"));
    }

    [Fact]
    public void MissingRemoveIsNoOpAndEveryAddAllocates()
    {
        var set = New();
        byte[] empty = set.ExportSnapshot();
        Ok(set.RemoveObserved("missing"));
        Assert.Equal(empty, set.ExportSnapshot());
        Assert.Equal(new Dot("A", 1), set.Add("").Dot);
        Assert.Equal(new Dot("A", 2), set.Add("").Dot);
        Assert.Equal(2, Rows(set).Length);
        Ok(set.RemoveObserved(""));
        Assert.Empty(set.GetVisibleElements());
        Assert.Equal(2, Rows(set).Length);
    }

    [Fact]
    public void UnicodeIsOrdinalAndNotNormalized()
    {
        var set = New();
        Ok(set.Add("é"));
        Ok(set.Add("e\u0301"));
        Ok(set.RemoveObserved("é"));
        Assert.False(set.Contains("é"));
        Assert.True(set.Contains("e\u0301"));
        Ok(set.Add(string.Concat(Enumerable.Repeat("😀", 32))));
        byte[] before = set.ExportSnapshot();
        foreach (string? invalid in new[] { null, "\ud800", "\udc00", new string('a', 129), string.Concat(Enumerable.Repeat("😀", 33)) })
        {
            Assert.Equal(ResultCode.InvalidElement, set.Add(invalid).Code);
            Assert.Equal(ResultCode.InvalidElement, set.RemoveObserved(invalid).Code);
            Assert.False(set.Contains(invalid));
            Assert.Equal(before, set.ExportSnapshot());
        }
    }
}
