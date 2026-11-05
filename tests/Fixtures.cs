using System.Text;
using System.Text.Json;
using Lofrano.BoundedObservedSet;
using Xunit;

namespace BoundedObservedSet.Tests;

internal static class Fixtures
{
    internal static ObservedSet New(string writer = "A", SetLimits? limits = null) =>
        new(writer, new[] { "A", "B", "C" }, limits);

    internal static void Ok(OperationResult result) => Assert.Equal(ResultCode.Success, result.Code);

    internal static ObservedSet Copy(ObservedSet source, string writer = "A")
    {
        var copy = New(writer);
        Ok(copy.ImportSnapshot(source.ExportSnapshot()));
        return copy;
    }

    internal static byte[] Wire(string a = "0", string b = "0", string c = "0", string facts = "[]") =>
        Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"members\":[\"A\",\"B\",\"C\"],\"counters\":[" +
            "{\"replica\":\"A\",\"counter\":" + a + "}," +
            "{\"replica\":\"B\",\"counter\":" + b + "}," +
            "{\"replica\":\"C\",\"counter\":" + c + "}],\"facts\":" + facts + "}");

    internal static string FactJson(string replica, long counter, string element, bool removed = false) =>
        JsonSerializer.Serialize(new { replica, counter, element, removed });

    internal static string[] Rows(ObservedSet set)
    {
        using var doc = JsonDocument.Parse(set.ExportSnapshot());
        return doc.RootElement.GetProperty("facts").EnumerateArray().Select(f =>
            $"{f.GetProperty("replica").GetString()}:{f.GetProperty("counter").GetInt64()}:{f.GetProperty("element").GetString()}:{f.GetProperty("removed").GetBoolean()}")
            .ToArray();
    }

    internal static long[] Counters(ObservedSet set)
    {
        using var doc = JsonDocument.Parse(set.ExportSnapshot());
        return doc.RootElement.GetProperty("counters").EnumerateArray()
            .Select(c => c.GetProperty("counter").GetInt64()).ToArray();
    }
}
