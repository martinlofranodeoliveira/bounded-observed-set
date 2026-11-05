using System.Text;

namespace Lofrano.BoundedObservedSet;

internal static class TextRules
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsReplica(string? value)
    {
        if (value is null || value.Length is < 1 or > 32)
            return false;
        foreach (char c in value)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
                return false;
        return true;
    }

    internal static bool IsElement(string? value)
    {
        if (value is null || value.Length > 128)
            return false;
        try
        {
            return StrictUtf8.GetByteCount(value) <= 128;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool IsUtf8(ReadOnlySpan<byte> value)
    {
        try
        {
            StrictUtf8.GetCharCount(value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
