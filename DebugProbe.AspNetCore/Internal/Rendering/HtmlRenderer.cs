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
        const int slowRequestThresholdMs = 1000;

        var rows = string.Join("", items.Select(x => $@"
        <tr data-url=""/debug/{Encode(x.Id)}""
            data-method=""{Encode(x.Method)}""
            data-status-family=""{x.StatusCode / 100}""
            data-search=""{Encode($"{x.Id} {x.Method} {x.Path} {x.Query} {x.StatusCode}")}""
            class=""clickable-row"">
            <td>{x.Timestamp:HH:mm:ss}</td>
            <td><span class=""method-pill"">{Encode(x.Method)}</span></td>
            <td>{Encode(string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}")}</td>
            <td><span class=""status {GetStatusClass(x.StatusCode)}"">{x.StatusCode}</span></td>
            <td>{x.DurationMs} ms</td>
        </tr>"
        ));

        if (string.IsNullOrEmpty(rows))
            rows = "<tr class='empty-row'><td colspan='5'>No data</td></tr>";

        var methodOptions = string.Join("", items
            .Select(x => x.Method)
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(method => method, StringComparer.OrdinalIgnoreCase)
            .Select(method => $@"<option value=""{Encode(method)}"">{Encode(method)}</option>"));

        var totalRequests = items.Count;
        var averageResponseMs = totalRequests == 0
            ? 0
            : (int)Math.Round(items.Average(x => x.DurationMs));
        var slowRequests = items.Count(x => x.DurationMs >= slowRequestThresholdMs);
        var errorRate = totalRequests == 0
            ? 0
            : items.Count(x => x.StatusCode >= 400) * 100d / totalRequests;

        return BuildLayout(EmbeddedResources.Index
            .Replace("{{rows}}", rows)
            .Replace("{{total_count}}", items.Count.ToString())
            .Replace("{{method_options}}", methodOptions)
            .Replace("{{total_requests}}", FormatCompactNumber(totalRequests))
            .Replace("{{avg_response_time}}", $"{averageResponseMs} ms")
            .Replace("{{slow_requests}}", FormatCompactNumber(slowRequests))
            .Replace("{{error_rate}}", $"{errorRate:0.#}%"));
    }

    public static string RenderDetailsPage(DebugEntry x, DebugEnvironment e, string req, string res)
    {
        var requestHeaders = string.Join(Environment.NewLine, x.RequestHeaders.Select(h => $"{h.Key}: {h.Value}"));
        var responseHeaders = string.Join(Environment.NewLine, x.ResponseHeaders.Select(h => $"{h.Key}: {h.Value}"));

        var pathWithQuery = string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}";

        var statusClass = GetStatusClass(x.StatusCode);

        var outgoingRequests = string.Join("", x.OutgoingRequests.Select(r => $@"
            <div class=""outgoing-request-item"">
                <div class=""outgoing-request-header"">
                    <div class=""outgoing-request-main"">
                        <span class=""method-pill"">
                            {Encode(r.Method)}
                        </span>
                        <span class=""outgoing-url"">
                            {Encode(r.Url)}
                        </span>
                    </div>
                    <div class=""outgoing-request-side"">
                        <span class=""status {GetStatusClass(r.StatusCode ?? 0)}"">
                            {(r.StatusCode.HasValue ? GetStatusText(r.StatusCode.Value) : "Failed")}
                        </span>
                        <span class=""outgoing-duration"">
                            {r.DurationMs} ms
                        </span>
                    </div>
                </div>
            </div>
        "));

        var content = EmbeddedResources.Details
            .Replace("{{method}}", Encode(x.Method))
            .Replace("{{path}}", Encode(pathWithQuery))
            .Replace("{{status}}", GetStatusText(x.StatusCode))
            .Replace("{{statusClass}}", statusClass)
            .Replace("{{responseGroupClass}}", GetResponseGroupClass(x.StatusCode))
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

            .Replace("{{outgoingRequests}}",
                string.IsNullOrWhiteSpace(outgoingRequests)
                    ? "<div class='empty-state'>No outgoing requests</div>"
                    : outgoingRequests)

            .Replace("{{requestUrl}}", Encode(string.IsNullOrEmpty(x.RequestUrl) ? "" : x.RequestUrl))
            .Replace("{{requestHeaders}}", Encode(requestHeaders))
            .Replace("{{request}}", Encode(string.IsNullOrEmpty(req) ? "" : req))

            .Replace("{{responseHeaders}}", Encode(responseHeaders))
            .Replace("{{response}}", Encode(string.IsNullOrEmpty(res) ? "" : res)

            );

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

    private static string GetResponseGroupClass(int statusCode)
    {
        return statusCode >= 400 ? "response-error" : "";
    }

    private static string FormatCompactNumber(int value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
            >= 1_000 => $"{value / 1_000d:0.#}K",
            _ => value.ToString()
        };
    }

}
