using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Internal.Utils;

internal static class RedactionUtils
{
    public static string RedactHeader(string name, string value, DebugProbeOptions options)
    {
        return IsMatch(name, options.RedactedHeaders) ? options.RedactionText : value;
    }

    public static string RedactQueryString(string? queryString, DebugProbeOptions options)
    {
        if (string.IsNullOrEmpty(queryString) || options.RedactedQueryParameters.Length == 0)
        {
            return queryString ?? string.Empty;
        }

        var prefix = queryString.StartsWith('?') ? "?" : string.Empty;
        var query = prefix.Length == 0 ? queryString : queryString[1..];

        return prefix + RedactQuery(query, options);
    }

    public static string RedactUrl(string? url, DebugProbeOptions options)
    {
        if (string.IsNullOrEmpty(url) || options.RedactedQueryParameters.Length == 0)
        {
            return url ?? string.Empty;
        }

        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? url[fragmentIndex..] : string.Empty;
        var withoutFragment = fragmentIndex >= 0 ? url[..fragmentIndex] : url;

        var queryIndex = withoutFragment.IndexOf('?');
        if (queryIndex < 0)
        {
            return url;
        }

        var beforeQuery = withoutFragment[..queryIndex];
        var query = withoutFragment[(queryIndex + 1)..];

        return $"{beforeQuery}?{RedactQuery(query, options)}{fragment}";
    }

    public static string RedactJsonFields(string? body, DebugProbeOptions options)
    {
        if (string.IsNullOrWhiteSpace(body) || options.RedactedJsonFields.Length == 0)
        {
            return body ?? string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(body);
            if (node is null)
            {
                return body;
            }

            RedactNode(node, options);

            return JsonSerializer.Serialize(
                node,
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }
        catch
        {
            return body;
        }
    }

    private static string RedactQuery(string query, DebugProbeOptions options)
    {
        if (query.Length == 0)
        {
            return query;
        }

        var parts = query.Split('&');

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var equalsIndex = part.IndexOf('=');
            var name = equalsIndex >= 0 ? part[..equalsIndex] : part;

            if (!IsMatch(DecodeQueryValue(name), options.RedactedQueryParameters))
            {
                continue;
            }

            parts[i] = equalsIndex >= 0? $"{name}={options.RedactionText}" : $"{name}={options.RedactionText}";
        }

        return string.Join("&", parts);
    }

    private static void RedactNode(JsonNode node, DebugProbeOptions options)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (IsMatch(property.Key, options.RedactedJsonFields))
                {
                    jsonObject[property.Key] = options.RedactionText;
                    continue;
                }

                if (property.Value is not null)
                {
                    RedactNode(property.Value, options);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    RedactNode(item, options);
                }
            }
        }
    }

    private static string DecodeQueryValue(string value)
    {
        return Uri.UnescapeDataString(value.Replace("+", " "));
    }

    private static bool IsMatch(string value, string[] candidates)
    {
        return candidates.Any(candidate =>
            string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }
}
