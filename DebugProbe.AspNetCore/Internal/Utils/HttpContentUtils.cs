namespace DebugProbe.AspNetCore.Internal.Utils;

internal static class HttpContentUtils
{
    public static bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    public static string Trim(string? value, int max = 2000)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return value.Length <= max? value : value.Substring(0, max);
    }
}
