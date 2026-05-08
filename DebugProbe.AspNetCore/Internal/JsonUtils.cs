using System.Text.Encodings.Web;
using System.Text.Json;

namespace DebugProbe.AspNetCore.Internal;

internal static class JsonUtils
{
    public static string Format(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        try
        {
            using var document = JsonDocument.Parse(json);

            return JsonSerializer.Serialize(
                document.RootElement,
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
}