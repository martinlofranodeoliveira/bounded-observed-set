using System.Text;
using Lofrano.BoundedObservedSet;
using Xunit;
using static BoundedObservedSet.Tests.Fixtures;

namespace BoundedObservedSet.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public void ExactCanonicalEmptyEncoding()
    {
        var set = New();
        Assert.Equal(Wire(), set.ExportSnapshot());
    }

    [Fact]
    public void RoundTripContinuesCounterAndDoesNotEmbedWriter()
    {
        var a = New();
        Ok(a.Add("x"));
        var restored = New();
        Ok(restored.ImportSnapshot(a.ExportSnapshot()));
        Assert.Equal(a.ExportSnapshot(), restored.ExportSnapshot());
        Assert.Equal(new Dot("A", 2), restored.Add("x").Dot);
        var b = New("B");
        Ok(b.ImportSnapshot(a.ExportSnapshot()));
        Assert.Equal(a.ExportSnapshot(), b.ExportSnapshot());
        Assert.Equal(new Dot("B", 1), b.Add("x").Dot);
    }

    [Fact]
    public void UnorderedInputBecomesCanonical()
    {
        byte[] input = Encoding.UTF8.GetBytes("{\"facts\":[{\"removed\":false,\"element\":\"z\",\"counter\":1,\"replica\":\"B\"},{\"removed\":true,\"element\":\"x\",\"counter\":1,\"replica\":\"A\"}],\"counters\":[{\"counter\":0,\"replica\":\"C\"},{\"counter\":1,\"replica\":\"B\"},{\"counter\":1,\"replica\":\"A\"}],\"members\":[\"C\",\"B\",\"A\"],\"schemaVersion\":1}");
        var set = New();
        Ok(set.ImportSnapshot(input));
        Assert.Equal(Wire(a: "1", b: "1", facts: "[" + FactJson("A", 1, "x", true) + "," + FactJson("B", 1, "z") + "]"), set.ExportSnapshot());
    }

    [Fact]
    public void MalformedInputsNeverReplacePriorState()
    {
        string empty = Encoding.UTF8.GetString(Wire());
        string validFact = FactJson("A", 1, "x");
        string seventeenMembers = string.Join(",", Enumerable.Range(0, 17).Select(i => $"\"A{i}\""));
        string seventeenCounters = string.Join(",", Enumerable.Range(0, 17).Select(i => $"{{\"replica\":\"A{i}\",\"counter\":0}}"));
        string tooManyFacts = string.Join(",", Enumerable.Range(1, 4097).Select(i => FactJson("A", i, "x")));
        string tooManyElements = string.Join(",", Enumerable.Range(1, 1025).Select(i => FactJson("A", i, "e" + i)));
        var invalid = new List<byte[]>
        {
            Array.Empty<byte>(), Encoding.UTF8.GetBytes("null"), Encoding.UTF8.GetBytes("[]"),
            new byte[] { 0xef, 0xbb, 0xbf }.Concat(Wire()).ToArray(),
            Encoding.UTF8.GetBytes(empty + "{}"),
            Encoding.UTF8.GetBytes(empty.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"extra\":0", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("\"members\":", "\"\\u006dembers\":[],\"members\":", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("[\"A\",\"B\",\"C\"]", "[\"A\",\"A\",\"C\"]", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("[\"A\",\"B\",\"C\"]", "[\"A\",\"B\",\"é\"]", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"members\":[],\"counters\":[],\"facts\":[]}"),
            Encoding.UTF8.GetBytes($"{{\"schemaVersion\":1,\"members\":[{seventeenMembers}],\"counters\":[{seventeenCounters}],\"facts\":[]}}"),
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"members\":[\"A\",\"B\",\"C\"],\"counters\":[{\"replica\":\"A\",\"counter\":0},{\"replica\":\"B\",\"counter\":0}],\"facts\":[]}"),
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"members\":[\"A\"],\"counters\":[{\"replica\":\"A\",\"counter\":0}],\"facts\":[[[[[[[[]]]]]]]]}"),
            Wire(a: "1", facts: "[" + FactJson("Z", 1, "x") + "]"),
            Wire(facts: "[" + validFact + "]"),
            Wire(a: "1", facts: "[" + FactJson("A", 2, "x") + "]"),
            Wire(a: "1", facts: "[" + validFact + "," + validFact + "]"),
            Wire(a: "1", facts: "[" + validFact.Replace("\"counter\":1", "\"counter\":1,\"\\u0063ounter\":1", StringComparison.Ordinal) + "]"),
            Wire(a: "1", facts: "[" + validFact.Replace("\"removed\":false", "\"removed\":0", StringComparison.Ordinal) + "]"),
            Wire(a: "1", facts: "[" + validFact.Replace("\"x\"", "\"\\uD800\"", StringComparison.Ordinal) + "]"),
            Wire(a: "1", facts: "[" + validFact.Replace("\"x\"", "\"\\uDC00\"", StringComparison.Ordinal) + "]"),
            Wire(a: "1", facts: "[" + FactJson("A", 0, "x") + "]"),
            Wire(a: "1", facts: "[" + FactJson("A", 1, new string('a', 129)) + "]"),
            Wire(a: "4097", facts: "[" + tooManyFacts + "]"),
            Wire(a: "1025", facts: "[" + tooManyElements + "]"),
            Encoding.UTF8.GetBytes(empty.Replace("\"counter\":0", "\"counter\":0,\"unknown\":0", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("\"replica\":\"B\"", "\"replica\":\"A\"", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(empty.Replace("\"facts\":[]", "\"facts\":[],", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes("/*comment*/" + empty),
            new byte[] { 0xff, 0xfe },
            new byte[] { (byte)'"', 0xc0, 0xaf, (byte)'"' }
        };
        foreach (string token in new[] { "-1", "-0", "1.0", "1e0", "\"1\"", "true", "null", "9223372036854775808" })
            invalid.Add(Wire(a: token));
        var set = New();
        Ok(set.Add("preserved"));
        byte[] before = set.ExportSnapshot();
        foreach (byte[] bytes in invalid)
        {
            Assert.Equal(ResultCode.InvalidSnapshot, set.ImportSnapshot(bytes).Code);
            Assert.Equal(before, set.ExportSnapshot());
        }
    }

    [Fact]
    public void InputSizeIsCheckedAndExportBuffersAreIndependent()
    {
        var set = New();
        Ok(set.Add("x"));
        byte[] before = set.ExportSnapshot();
        Assert.Equal(ResultCode.SnapshotTooLarge, set.ImportSnapshot(new byte[ObservedSet.MaxSnapshotBytes + 1]).Code);
        Assert.Equal(before, set.ExportSnapshot());
        byte[] external = set.ExportSnapshot();
        external[0] = 0;
        Assert.Equal(before, set.ExportSnapshot());
    }

    [Fact]
    public void WorstCaseControlEscapingFitsAndRoundTripsAtHardCap()
    {
        var set = New();
        string controls = new('\0', 128);
        for (int i = 0; i < 4096; i++)
            Ok(set.Add(controls));
        byte[] bytes = set.ExportSnapshot();
        Assert.InRange(bytes.Length, 3_000_000, ObservedSet.MaxSnapshotBytes);
        var copy = New("C");
        Ok(copy.ImportSnapshot(bytes));
        Assert.Equal(bytes, copy.ExportSnapshot());
        Assert.Equal(ResultCode.HistoryLimit, copy.Add(controls).Code);
    }
}
