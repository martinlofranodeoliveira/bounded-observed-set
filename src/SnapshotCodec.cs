using System.Text.Json;

namespace Lofrano.BoundedObservedSet;

internal static class SnapshotCodec
{
    internal const int ByteLimit = 4_194_304;

    internal static byte[] Write(ReplicaState state)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("members");
            foreach (string member in state.Members)
                writer.WriteStringValue(member);
            writer.WriteEndArray();
            writer.WriteStartArray("counters");
            foreach (string member in state.Members)
            {
                writer.WriteStartObject();
                writer.WriteString("replica", member);
                writer.WriteNumber("counter", state.Counters[member]);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("facts");
            foreach (var entry in state.Facts.OrderBy(p => p.Key.Replica, StringComparer.Ordinal)
                         .ThenBy(p => p.Key.Counter))
            {
                writer.WriteStartObject();
                writer.WriteString("replica", entry.Key.Replica);
                writer.WriteNumber("counter", entry.Key.Counter);
                writer.WriteString("element", entry.Value.Element);
                writer.WriteBoolean("removed", entry.Value.Removed);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        // The schema's hard caps imply less than 3.7 MB even with maximal escaping.
        if (stream.Length > ByteLimit)
            throw new InvalidOperationException("Snapshot size invariant violated.");
        return stream.ToArray();
    }

    internal static ResultCode Read(ReadOnlySpan<byte> bytes, out ReplicaState? state)
    {
        state = null;
        if (bytes.Length > ByteLimit)
            return ResultCode.SnapshotTooLarge;
        if (!TextRules.IsUtf8(bytes))
            return ResultCode.InvalidSnapshot;
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 8,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            JsonElement root = document.RootElement;
            if (!Fields(root, "schemaVersion", "members", "counters", "facts") ||
                !UnsignedInteger(root.GetProperty("schemaVersion"), out long version) || version != 1)
                return ResultCode.InvalidSnapshot;

            JsonElement membersJson = root.GetProperty("members");
            if (membersJson.ValueKind != JsonValueKind.Array || membersJson.GetArrayLength() is < 1 or > 16)
                return ResultCode.InvalidSnapshot;
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in membersJson.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    return ResultCode.InvalidSnapshot;
                string? member = entry.GetString();
                if (!TextRules.IsReplica(member) || !members.Add(member!))
                    return ResultCode.InvalidSnapshot;
            }
            var candidate = new ReplicaState(members.Order(StringComparer.Ordinal).ToArray());
            JsonElement counters = root.GetProperty("counters");
            if (counters.ValueKind != JsonValueKind.Array || counters.GetArrayLength() != members.Count)
                return ResultCode.InvalidSnapshot;
            var seenCounters = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in counters.EnumerateArray())
            {
                if (!Fields(entry, "replica", "counter") ||
                    entry.GetProperty("replica").ValueKind != JsonValueKind.String)
                    return ResultCode.InvalidSnapshot;
                string member = entry.GetProperty("replica").GetString()!;
                if (!members.Contains(member) || !seenCounters.Add(member) ||
                    !UnsignedInteger(entry.GetProperty("counter"), out long counter))
                    return ResultCode.InvalidSnapshot;
                candidate.Counters[member] = counter;
            }

            JsonElement facts = root.GetProperty("facts");
            if (facts.ValueKind != JsonValueKind.Array || facts.GetArrayLength() > 4096)
                return ResultCode.InvalidSnapshot;
            var elements = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in facts.EnumerateArray())
            {
                if (!Fields(entry, "replica", "counter", "element", "removed") ||
                    entry.GetProperty("replica").ValueKind != JsonValueKind.String ||
                    entry.GetProperty("element").ValueKind != JsonValueKind.String)
                    return ResultCode.InvalidSnapshot;
                string member = entry.GetProperty("replica").GetString()!;
                string element = entry.GetProperty("element").GetString()!;
                JsonElement removed = entry.GetProperty("removed");
                if (!members.Contains(member) || !TextRules.IsElement(element) ||
                    !UnsignedInteger(entry.GetProperty("counter"), out long counter) || counter == 0 ||
                    counter > candidate.Counters[member] ||
                    removed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return ResultCode.InvalidSnapshot;
                if (!candidate.Facts.TryAdd(new Dot(member, counter), new Fact(element, removed.GetBoolean())))
                    return ResultCode.InvalidSnapshot;
                elements.Add(element);
                if (elements.Count > 1024)
                    return ResultCode.InvalidSnapshot;
            }
            state = candidate;
            return ResultCode.Success;
        }
        catch (JsonException)
        {
            return ResultCode.InvalidSnapshot;
        }
        catch (InvalidOperationException)
        {
            // Includes invalid escaped Unicode detected while decoding JSON strings.
            return ResultCode.InvalidSnapshot;
        }
        catch (ArgumentException)
        {
            return ResultCode.InvalidSnapshot;
        }
    }

    private static bool Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            // Name is decoded: "counter" and "\u0063ounter" are the same field.
            string name = property.Name;
            if (!allowed.Contains(name, StringComparer.Ordinal) || !seen.Add(name))
                return false;
        }
        return seen.Count == allowed.Length;
    }

    private static bool UnsignedInteger(JsonElement value, out long number)
    {
        number = 0;
        if (value.ValueKind != JsonValueKind.Number)
            return false;
        foreach (char c in value.GetRawText())
            if (c is < '0' or > '9')
                return false;
        return value.TryGetInt64(out number);
    }
}
