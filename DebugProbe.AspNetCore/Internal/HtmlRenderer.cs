using System.Net;
using DebugProbe.AspNetCore.Models;

namespace DebugProbe.AspNetCore.Internal;

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
        <tr onclick=""window.location='/debug/{x.Id}'"" style=""cursor:pointer"">
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

    public static string RenderDetailsPage(DebugEntry x, string req, string res)
    {
        var headers = string.Join("", x.Headers.Select(h =>
            $"<tr><td>{Encode(h.Key)}</td><td>{Encode(h.Value)}</td></tr>"));

        var pathWithQuery = string.IsNullOrEmpty(x.Query)
            ? x.Path
            : $"{x.Path}{x.Query}";

        var content = EmbeddedResources.Details
            .Replace("{{method}}", Encode(x.Method))
            .Replace("{{path}}", Encode(pathWithQuery))
            .Replace("{{status}}", x.StatusCode.ToString())
            .Replace("{{tradeId}}", x.Id.ToString())

            .Replace("{{time}}", x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{local}}", x.Timestamp.ToLocalTime().ToString("HH:mm:ss"))

            .Replace("{{durationMs}}", x.DurationMs.ToString())
            .Replace("{{requestSize}}", x.RequestSize.ToString())
            .Replace("{{responseSize}}", x.ResponseSize.ToString())

            .Replace("{{env}}", Encode(x.Environment))
            .Replace("{{culture}}", Encode(x.Culture))

            .Replace("{{machineName}}", Encode(x.MachineName))
            .Replace("{{timeZone}}", Encode(x.TimeZone))
            .Replace("{{decimalSeparator}}", Encode(x.DecimalSeparator))
            .Replace("{{dateFormat}}", x.DateFormat ?? "")
            .Replace("{{assemblyVersion}}", Encode(x.AssemblyVersion))





            .Replace("{{requestUrl}}", Encode(string.IsNullOrEmpty(x.RequestUrl) ? "(empty)" : x.RequestUrl))
            .Replace("{{request}}", Encode(string.IsNullOrEmpty(req) ? "(empty)" : req))
            .Replace("{{response}}", Encode(string.IsNullOrEmpty(res) ? "(empty)" : res))
            .Replace("{{headers}}", headers);

        return BuildLayout(content);
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? "");
    }
}
