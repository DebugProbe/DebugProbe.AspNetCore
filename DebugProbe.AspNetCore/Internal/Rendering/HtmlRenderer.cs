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
        var averageResponseMs = totalRequests == 0 ? 0 : (int)Math.Round(items.Average(x => x.DurationMs));
        var slowRequests = items.Count(x => x.DurationMs >= slowRequestThresholdMs);
        var errorRate = totalRequests == 0 ? 0 : items.Count(x => x.StatusCode >= 400) * 100d / totalRequests;

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
        var pathWithQuery = string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}";

        var statusClass = GetStatusClass(x.StatusCode);

        var incomingRequest = BuildTraceCard(
            "Incoming Request",
            x.Method,
            string.IsNullOrWhiteSpace(x.RequestUrl) ? pathWithQuery : x.RequestUrl,
            "request",
            statusCode: x.StatusCode,
            durationMs: x.DurationMs,
            details:
            [
                BuildPayloadSection("URL", string.IsNullOrWhiteSpace(x.RequestUrl) ? pathWithQuery : x.RequestUrl, "url"),
                BuildHeaderSection("Headers", x.RequestHeaders),
                BuildPayloadSection("Body", req, "body")
            ]);

        var incomingResponse = BuildTraceCard(
            "Final Response",
            "",
            "",
            x.StatusCode >= 400 ? "response error" : "response",
            [
                BuildHeaderSection("Headers", x.ResponseHeaders),
                BuildPayloadSection("Body", res, "body")
            ]);

        var outgoingRequests = string.Join("", x.OutgoingRequests.Select(BuildOutgoingRequestCard));

        var content = EmbeddedResources.Details
            .Replace("{{method}}", Encode(x.Method))
            .Replace("{{path}}", Encode(pathWithQuery))
            .Replace("{{status}}", GetStatusText(x.StatusCode))
            .Replace("{{statusClass}}", statusClass)
            .Replace("{{traceId}}", x.Id.ToString())

            .Replace("{{time}}", x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{local}}", x.Timestamp.ToLocalTime().ToString("HH:mm:ss"))

            .Replace("{{durationMs}}", x.DurationMs.ToString())
            .Replace("{{completed}}", x.Timestamp.AddMilliseconds(x.DurationMs).ToLocalTime().ToString("HH:mm:ss"))
            .Replace("{{dependencyCount}}", x.OutgoingRequests.Count.ToString())

            .Replace("{{env}}", Encode(e.Environment))
            .Replace("{{culture}}", Encode(e.Culture))

            .Replace("{{machineName}}", Encode(e.MachineName))
            .Replace("{{timeZone}}", Encode(e.TimeZone))
            .Replace("{{decimalSeparator}}", Encode(e.DecimalSeparator))
            .Replace("{{dateFormat}}", e.DateFormat ?? "")
            .Replace("{{assemblyVersion}}", Encode(e.AssemblyVersion))
            .Replace("{{outgoingRequests}}",
                string.IsNullOrWhiteSpace(outgoingRequests)
                    ? "<div class='empty-state trace-empty'>No outgoing dependency calls</div>"
                    : outgoingRequests)
            .Replace("{{incomingRequest}}", incomingRequest)
            .Replace("{{incomingResponse}}", incomingResponse);

        return BuildLayout(content);
    }

    public static string RenderComparePage(string localTraceId, string baseUrl, string traceId)
    {
        var content = $@"
        <div class=""container compare-page"">
            <section class=""trace-card compare-card"" aria-label=""Compare trace result"">
                <div class=""trace-card-main"">
                    <div class=""trace-card-header"">
                        <div class=""trace-card-title"">
                            <span class=""trace-dot"" aria-hidden=""true""></span>
                            <span class=""trace-label"">Compare Trace</span>
                        </div>
                    </div>
                    <input id=""localTraceId"" type=""hidden"" value=""{Encode(localTraceId)}"" />
                    <input id=""baseUrl"" type=""hidden"" value=""{Encode(baseUrl)}"" />
                    <input id=""compareId"" type=""hidden"" value=""{Encode(traceId)}"" />
                    <div id=""compareResult"">
                        <div class=""compare-message"">Comparing...</div>
                    </div>
                </div>
            </section>
        </div>";

        return BuildLayout(content);
    }

    private static string BuildOutgoingRequestCard(DebugOutgoingRequest request)
    {
        var classes = request.StatusCode >= 400 || !string.IsNullOrWhiteSpace(request.Exception)
            ? "dependency error"
            : "dependency";

        var details = new List<string>
        {
            BuildHeaderSection("Request Headers", request.RequestHeaders),
            BuildPayloadSection("Request Body", request.RequestBody, "body"),
            BuildHeaderSection("Response Headers", request.ResponseHeaders),
            BuildPayloadSection("Response Body", request.ResponseBody, "body")
        };

        if (!string.IsNullOrWhiteSpace(request.Exception))
        {
            details.Add(BuildPayloadSection("Exception", request.Exception, "exception", open: true));
        }

        return BuildTraceCard(
            "Http Client",
            request.Method,
            request.Url,
            classes,
            statusCode: request.StatusCode,
            statusText: request.StatusCode.HasValue ? null : "Failed",
            durationMs: request.DurationMs,
            details: details);
    }

    private static string BuildTraceCard(string label, string method, string target, string classes, IEnumerable<string> details, int? statusCode = null, string? statusText = null, long? durationMs = null)
    {
        var targetHost = GetDisplayTarget(target);
        var status = statusCode.HasValue
            ? $@"<span class=""status {GetStatusClass(statusCode.Value)}"">{Encode(GetStatusText(statusCode.Value))}</span>"
            : !string.IsNullOrWhiteSpace(statusText)
                ? $@"<span class=""status status-500"">{Encode(statusText)}</span>"
            : "";
        var duration = durationMs.HasValue ? $@"<span>{durationMs.Value} ms</span>" : "";

        var methodPill = !string.IsNullOrWhiteSpace(method)  ? $@"<span class=""method-pill"">{Encode(method)}</span>" : "";

        return $@"
        <article class=""trace-card {Encode(classes)}"">
            <div class=""trace-card-main"">
                <div class=""trace-card-header"">
                    <div class=""trace-card-title"">
                        <span class=""trace-dot"" aria-hidden=""true""></span>
                        <span class=""trace-label"">{Encode(label)}</span>
                         {methodPill}
                        <strong title=""{Encode(target)}"">{Encode(targetHost)}</strong>
                    </div>
                    <div class=""trace-card-meta"">
                        {status}
                        {duration}
                    </div>
                </div>
                <div class=""trace-details"">
                    {string.Join("", details)}
                </div>
            </div>
        </article>";
    }

    private static string BuildHeaderSection(string title, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return BuildEmptySection(title, "No headers captured");
        }

        var rows = string.Join("", headers.Select(header => $@"
            <div class=""header-row"">
                <span>{Encode(header.Key)}</span>
                <code>{Encode(header.Value)}</code>
            </div>"));

        return $@"
        <details class=""payload-panel"">
            <summary>
                <span>{Encode(title)}</span>
                <small>{headers.Count} headers</small>
            </summary>
            <div class=""headers-grid"">
                {rows}
            </div>
        </details>";
    }

    private static string BuildPayloadSection(string title, string? value, string kind, bool open = false)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : JsonUtils.Format(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildEmptySection(title, "Empty");
        }

        return $@"
        <details class=""payload-panel""{(open ? " open" : "")}>
            <summary>
                <span>{Encode(title)}</span>
                <small>{Encode(kind)} - {FormatBytes(text.Length)}</small>
            </summary>
            <div class=""code-block"">
                <button class=""copy-btn"" type=""button"" onclick=""copyText(this)"">Copy</button>
                <pre>{Encode(text)}</pre>
            </div>
        </details>";
    }

    private static string BuildEmptySection(string title, string message)
    {
        return $@"
        <details class=""payload-panel payload-panel-empty"">
            <summary>
                <span>{Encode(title)}</span>
                <small>{Encode(message)}</small>
            </summary>
        </details>";
    }

    private static string GetDisplayTarget(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.IsNullOrWhiteSpace(uri.PathAndQuery) ? uri.Host : $"{uri.Host}{uri.PathAndQuery}";
        }

        return value;
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

    private static string FormatCompactNumber(int value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
            >= 1_000 => $"{value / 1_000d:0.#}K",
            _ => value.ToString()
        };
    }

    private static string FormatBytes(int value)
    {
        return value switch
        {
            >= 1_048_576 => $"{value / 1_048_576d:0.#} MB",
            >= 1024 => $"{value / 1024d:0.#} KB",
            _ => $"{value} B"
        };
    }

}
