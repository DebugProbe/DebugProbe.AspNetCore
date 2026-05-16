using System.Text.Json;
using DebugProbe.AspNetCore.Models;

namespace DebugProbe.AspNetCore.Internal.Compare;

/// <summary>
/// Compares two DebugEntry instances and produces a list of differences,
/// including JSON-aware comparison of response bodies.
/// </summary>
internal static class DebugEntryComparer
{
    private const string RequestBodyPath = "RequestBody";
    private const string ResponseBodyPath = "ResponseBody";

    public static List<object> Compare(DebugEntry a, DebugEntry b)
    {
        var diffs = new List<object>();

        if (a.StatusCode != b.StatusCode)
            AddDiff(diffs, "Status", a.StatusCode, b.StatusCode, "meta");

        CompareBody(a.RequestBody, b.RequestBody, RequestBodyPath, diffs);
        CompareBody(a.ResponseBody, b.ResponseBody, ResponseBodyPath, diffs);

        return diffs;
    }

    private static void CompareBody(string? localBody, string? remoteBody, string path, List<object> diffs)
    {
        if (string.IsNullOrWhiteSpace(localBody) && string.IsNullOrWhiteSpace(remoteBody))
            return;

        try
        {
            using var localJson = JsonDocument.Parse(localBody ?? "{}");
            using var remoteJson = JsonDocument.Parse(remoteBody ?? "{}");

            CompareJson(localJson.RootElement, remoteJson.RootElement, path, diffs);
        }
        catch
        {
            if (localBody != remoteBody)
                AddDiff(diffs, path, localBody, remoteBody, GetType(path));
        }
    }

    private static void CompareJson(JsonElement a, JsonElement b, string path, List<object> diffs)
    {
        if (a.ValueKind != b.ValueKind)
        {
            AddDiff(diffs, Clean(path), a.ToString(), b.ToString(), GetType(path));
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObject(a, b, path, diffs);
                break;

            case JsonValueKind.Array:
                CompareArray(a, b, path, diffs);
                break;

            default:
                var aVal = a.ToString();
                var bVal = b.ToString();

                if (aVal != bVal)
                    AddDiff(diffs, Clean(path), aVal, bVal, GetType(path));
                break;
        }
    }

    private static void CompareObject(JsonElement a, JsonElement b, string path, List<object> diffs)
    {
        var remoteProperties = new Dictionary<string, JsonElement>();
        foreach (var prop in b.EnumerateObject())
            remoteProperties[prop.Name] = prop.Value;

        foreach (var prop in a.EnumerateObject())
        {
            var childPath = $"{path}.{prop.Name}";

            if (remoteProperties.Remove(prop.Name, out var remoteProp))
                CompareJson(prop.Value, remoteProp, childPath, diffs);
            else
                AddMissingDiff(childPath, prop.Value, null, diffs);
        }

        foreach (var prop in remoteProperties)
            AddMissingDiff($"{path}.{prop.Key}", null, prop.Value, diffs);
    }

    private static void CompareArray(JsonElement a, JsonElement b, string path, List<object> diffs)
    {
        var lenA = a.GetArrayLength();
        var lenB = b.GetArrayLength();
        var sharedLength = Math.Min(lenA, lenB);

        for (var i = 0; i < sharedLength; i++)
            CompareJson(a[i], b[i], $"{path}[{i}]", diffs);

        if (lenA == lenB)
            return;

        AddDiff(diffs, Clean(path), $"Length: {lenA}", $"Length: {lenB}", GetType(path));

        for (var i = sharedLength; i < lenA; i++)
            AddMissingDiff($"{path}[{i}]", a[i], null, diffs);

        for (var i = sharedLength; i < lenB; i++)
            AddMissingDiff($"{path}[{i}]", null, b[i], diffs);
    }

    private static void AddMissingDiff(string path, JsonElement? local, JsonElement? remote, List<object> diffs)
    {
        AddDiff(
            diffs,
            Clean(path),
            local.HasValue ? local.Value.ToString() : "(missing)",
            remote.HasValue ? remote.Value.ToString() : "(missing)",
            GetType(path));
    }

    private static void AddDiff(List<object> diffs, string field, object? local, object? remote, string type)
    {
        diffs.Add(new { field, local, remote, type });
    }

    private static string GetType(string path)
    {
        return path.StartsWith(RequestBodyPath) ? "request" : "response";
    }

    private static string Clean(string path)
    {
        if (path.StartsWith($"{ResponseBodyPath}."))
            return path.Substring($"{ResponseBodyPath}.".Length);

        if (path == ResponseBodyPath)
            return "";

        if (path.StartsWith($"{RequestBodyPath}."))
            return path.Substring($"{RequestBodyPath}.".Length);

        if (path == RequestBodyPath)
            return "";

        return path;
    }
}
