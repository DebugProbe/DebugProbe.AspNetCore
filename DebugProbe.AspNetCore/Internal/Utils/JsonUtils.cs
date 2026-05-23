using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DebugProbe.AspNetCore.Internal.Utils;

internal static class JsonUtils
{
    public static string Format(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        try
        {
            var node = JsonNode.Parse(json);

            return JsonSerializer.Serialize(
                ExpandJsonStrings(node),
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }
        catch
        {
            return json;
        }
    }

    public static bool IsValidJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            JsonDocument.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonNode? ExpandJsonStrings(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            var expandedObject = new JsonObject();

            foreach (var property in jsonObject)
            {
                expandedObject[property.Key] = ExpandJsonStrings(property.Value);
            }

            return expandedObject;
        }

        if (node is JsonArray jsonArray)
        {
            var expandedArray = new JsonArray();

            foreach (var item in jsonArray)
            {
                expandedArray.Add(ExpandJsonStrings(item));
            }

            return expandedArray;
        }

        if (node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text) &&
            TryParseNestedJson(text, out var nested))
        {
            return ExpandJsonStrings(nested);
        }

        return node?.DeepClone();
    }

    private static bool TryParseNestedJson(string value, out JsonNode? node)
    {
        node = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var looksLikeJson =
            (trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']'));

        if (!looksLikeJson)
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(trimmed);
            return node is not null;
        }
        catch
        {
            return false;
        }
    }
}
