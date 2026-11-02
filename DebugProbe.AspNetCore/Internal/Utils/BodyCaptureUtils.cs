using System.Net.Http.Headers;
using System.Text;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Internal.Utils;

internal static class BodyCaptureUtils
{
    public const string BinaryBodyMessage = "[Body not captured: non-text content]";
    public const string TruncatedRedactionMessage = "[Body not captured: truncated content cannot be redacted]";

    public static string Format(ReadOnlySpan<byte> bytes, long totalBytes, string? contentType, DebugProbeOptions options)
    {
        var limit = options.MaxBodySizeBytes;
        if (limit <= 0 || totalBytes == 0)
        {
            return string.Empty;
        }

        if (!HttpContentUtils.IsTextContent(contentType))
        {
            return BinaryBodyMessage;
        }

        var truncated = totalBytes > limit;
        
        // Incomplete JSON cannot be reliably redacted. Never store a potentially sensitive prefix.
        if (truncated && options.RedactedJsonFields.Length > 0)
        {
            return TruncatedRedactionMessage;
        }

        var encoding = GetEncoding(contentType);
        var prefix = bytes[..Math.Min(bytes.Length, limit)];
        var chars = new char[encoding.GetMaxCharCount(prefix.Length)];

        encoding.GetDecoder().Convert(prefix, chars, flush: !truncated, out _, out var charsUsed, out _);
        
        var body = RedactionUtils.RedactJsonFields(new string(chars, 0, charsUsed), options);
        
        return truncated ? body + "[truncated]" : body;
    }

    private static Encoding GetEncoding(string? contentType)
    {
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsed) && !string.IsNullOrWhiteSpace(parsed.CharSet))
        {
            try
            {
                return Encoding.GetEncoding(parsed.CharSet.Trim('"'));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Missing or unsupported charsets use UTF-8 for all capture paths.
            }
        }

        return Encoding.UTF8;
    }
}
