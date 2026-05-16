using System.Net;
using DebugProbe.AspNetCore.Internal.Resources;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;

namespace DebugProbe.AspNetCore.Internal.Rendering;

/// <summary>
/// Renders DebugProbe UI pages (layout, index, details) using embedded HTML templates.
/// </summary>
internal static class HtmlRenderer
{
    public static string Env { get; } = EnvironmentUtils.TryGetEnvironment();

    public static string BuildLayout(string content)
    {
        var envBlock = string.IsNullOrWhiteSpace(Env) ? "" : $"<span class=\"env\">{Encode(Env)}</span>";

        return EmbeddedResources.Layout
            .Replace("{{styles}}", $"<style>{EmbeddedResources.Css}</style>")
            .Replace("{{content}}", content)
            .Replace("{{env_block}}", envBlock);
    }

    public static string RenderIndexPage(List<DebugEntry> items)
    {
        var rows = string.Join("", items.Select(x => $@"
        <tr data-url=""/debug/{Encode(x.Id)}"" class=""clickable-row"">
            <td>{x.Timestamp:HH:mm:ss}</td>
            <td>{Encode(x.Method)}</td>
            <td>{Encode(x.Path)}</td>
            <td style=""color:{(x.StatusCode >= 400 ? "#e74c3c" : "#2ecc71")}; font-weight:bold;"">
                {x.StatusCode}
            </td>
        </tr>"
        ));

        if (string.IsNullOrEmpty(rows))
            rows = "<tr><td colspan='4'>No data</td></tr>";

        return BuildLayout(EmbeddedResources.Index.Replace("{{rows}}", rows));
    }

    public static string RenderDetailsPage(DebugEntry x, DebugEnvironment e, string req, string res)
    {
        var headers = string.Join("", x.Headers.Select(h =>
            $"<tr><td>{Encode(h.Key)}</td><td>{Encode(h.Value)}</td></tr>"));

        var pathWithQuery = string.IsNullOrEmpty(x.Query)
            ? x.Path
            : $"{x.Path}{x.Query}";

        var statusClass = GetStatusClass(x.StatusCode);

        var content = EmbeddedResources.Details
            .Replace("{{method}}", Encode(x.Method))
            .Replace("{{path}}", Encode(pathWithQuery))
            .Replace("{{status}}", GetStatusText(x.StatusCode))
            .Replace("{{statusClass}}", statusClass)
            .Replace("{{responseStatusCode}}", x.StatusCode.ToString())
            .Replace("{{traceId}}", x.Id.ToString())

            .Replace("{{time}}", x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{local}}", x.Timestamp.ToLocalTime().ToString("HH:mm:ss"))

            .Replace("{{durationMs}}", x.DurationMs.ToString())
            .Replace("{{requestSize}}", x.RequestSize.ToString())
            .Replace("{{responseSize}}", x.ResponseSize.ToString())

            .Replace("{{env}}", Encode(e.Environment))
            .Replace("{{culture}}", Encode(e.Culture))

            .Replace("{{machineName}}", Encode(e.MachineName))
            .Replace("{{timeZone}}", Encode(e.TimeZone))
            .Replace("{{decimalSeparator}}", Encode(e.DecimalSeparator))
            .Replace("{{dateFormat}}", e.DateFormat ?? "")
            .Replace("{{assemblyVersion}}", Encode(e.AssemblyVersion))

            .Replace("{{requestUrl}}", Encode(string.IsNullOrEmpty(x.RequestUrl) ? "" : x.RequestUrl))
            .Replace("{{requestType}}", GetPayloadType(req))
            .Replace("{{requestTypeClass}}", GetPayloadTypeClass(req))
            .Replace("{{request}}", Encode(string.IsNullOrEmpty(req) ? "" : req))

            .Replace("{{responseType}}", GetPayloadType(res))
            .Replace("{{responseTypeClass}}", GetPayloadTypeClass(res))
            .Replace("{{response}}", Encode(string.IsNullOrEmpty(res) ? "" : res))

            .Replace("{{headers}}", headers);

        return BuildLayout(content);
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }

    private static string GetStatusText(int statusCode)
    {
        return $"{statusCode} {(HttpStatusCode)statusCode}";
    }

    private static string GetStatusClass(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 300 => "status-200",
            >= 300 and < 400 => "status-300",
            >= 400 and < 500 => "status-400",
            >= 500 => "status-500",
            _ => ""
        };
    }

    private static string GetPayloadType(string value)
    {
        if (IsCapturePlaceholder(value))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "Empty";
        }

        if (JsonUtils.IsValidJson(value))
        {
            return "JSON";
        }

        return LooksLikeJson(value) ? "Invalid JSON" : "Plain Text";
    }

    private static string GetPayloadTypeClass(string value)
    {
        if (IsCapturePlaceholder(value))
        {
            return "payload-hidden";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "payload-empty";
        }

        if (JsonUtils.IsValidJson(value))
        {
            return "payload-json";
        }

        return LooksLikeJson(value) ? "payload-invalid-json" : "payload-text";
    }

    private static bool IsCapturePlaceholder(string value)
    {
        return string.Equals(value, "[Body too large]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();

        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

}
