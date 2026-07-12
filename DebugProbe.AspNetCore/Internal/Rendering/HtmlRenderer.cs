using System.Net;
using DebugProbe.AspNetCore.Internal.Resources;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Options;

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

    public static string RenderIndexPage(List<DebugEntry> items, DebugProbeOptions? options = null)
    {
        options ??= new DebugProbeOptions();
        var slowRequestThresholdMs = options.SlowRequestThresholdMs;

        var rows = string.Join("", items.Select(x =>
        {
            var pathWithQuery = string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}";
            var badge = RenderSlowBadge(TimeSpan.FromMilliseconds(x.DurationMs), options);
            var badgeHtml = string.IsNullOrEmpty(badge) ? "" : " " + badge;

            return $@"
        <tr data-url=""/debug/{Encode(x.Id)}""
            data-method=""{Encode(x.Method)}""
            data-status-family=""{x.StatusCode / 100}""
            data-search=""{Encode($"{x.Id} {x.Method} {x.Path} {x.Query} {x.StatusCode}")}""
            class=""clickable-row"">
            <td>{x.Timestamp:HH:mm:ss}</td>
            <td><span class=""method-pill"">{Encode(x.Method)}</span></td>
            <td class=""request-path""><span class=""request-path-value"" title=""{Encode(pathWithQuery)}"">{Encode(pathWithQuery)}</span></td>
            <td><span class=""status {GetStatusClass(x.StatusCode)}"">{x.StatusCode}</span></td>
            <td>{x.DurationMs} ms{badgeHtml}</td>
        </tr>";
        }));

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
        var slowRequests = slowRequestThresholdMs > 0 ? items.Count(x => x.DurationMs >= slowRequestThresholdMs) : 0;
        var errorRate = totalRequests == 0 ? 0 : items.Count(x => x.StatusCode >= 400) * 100d / totalRequests;

        var exceptionPanel = "";
        var store = DebugEntryStore.Instance;
        if (store != null && !store.ExceptionGroups.IsEmpty)
        {
            var sortedGroups = store.ExceptionGroups.Values
                .OrderByDescending(g => g.Count)
                .ToList();

            var groupRows = string.Join("", sortedGroups.Select(g => $@"
            <tr>
                <td style=""font-weight: 600; color: #b42318; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;"" title=""{Encode(g.Type)}"">{Encode(g.Type)}</td>
                <td class=""request-path""><span class=""request-path-value"" title=""{Encode(g.SampleMessage)}"">{Encode(g.SampleMessage)}</span></td>
                <td><strong>{g.Count}</strong></td>
                <td>{g.LastSeen.ToLocalTime():yyyy-MM-dd HH:mm:ss}</td>
            </tr>"));

            exceptionPanel = $@"
        <h3>Exception Groups</h3>
        <div class=""table-wrap"" style=""margin-bottom: 24px;"">
            <table style=""table-layout: fixed;"">
                <thead>
                    <tr>
                        <th style=""width: 25%;"">Type</th>
                        <th style=""width: 45%;"">Sample Message</th>
                        <th style=""width: 10%;"">Count</th>
                        <th style=""width: 20%;"">Last Seen</th>
                    </tr>
                </thead>
                <tbody>
                    {groupRows}
                </tbody>
            </table>
        </div>";
        }

        var pageHtml = EmbeddedResources.Index;
        if (!string.IsNullOrEmpty(exceptionPanel))
        {
            var idx = pageHtml.IndexOf("<div class=\"table-wrap\">");
            if (idx >= 0)
            {
                pageHtml = pageHtml.Insert(idx, exceptionPanel);
            }
        }

        return BuildLayout(pageHtml
            .Replace("{{rows}}", rows)
            .Replace("{{total_count}}", items.Count.ToString())
            .Replace("{{method_options}}", methodOptions)
            .Replace("{{total_requests}}", FormatCompactNumber(totalRequests))
            .Replace("{{avg_response_time}}", $"{averageResponseMs} ms")
            .Replace("{{slow_requests}}", FormatCompactNumber(slowRequests))
            .Replace("{{error_rate}}", $"{errorRate:0.#}%"));
    }

    public static string RenderDetailsPage(DebugEntry x, DebugEnvironment e, string req, string res, DebugProbeOptions? options = null)
    {
        options ??= new DebugProbeOptions();
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
            ],
            dataMethod: x.Method,
            dataUrl: string.IsNullOrWhiteSpace(x.RequestUrl) ? pathWithQuery : x.RequestUrl,
            dataHeaders: System.Text.Json.JsonSerializer.Serialize(x.RequestHeaders),
            dataBody: x.RequestBody,
            options: options);

        var incomingResponse = BuildTraceCard(
            "Final Response",
            "",
            "",
            x.StatusCode >= 400 ? "response error" : "response",
            details:
            [
                BuildHeaderSection("Headers", x.ResponseHeaders),
                BuildPayloadSection("Body", res, "body")
            ],
            options: options);

        var waterfall = BuildWaterfallSection(x, options);

        var outgoingRequests = string.Join("", x.OutgoingRequests.Select(r => BuildOutgoingRequestCard(r, options)));

        var combinedOutgoing = waterfall + outgoingRequests;

        var content = EmbeddedResources.Details
            .Replace("{{method}}", Encode(x.Method))
            .Replace("{{path}}", Encode(pathWithQuery))
            .Replace("{{status}}", GetStatusText(x.StatusCode))
            .Replace("{{statusClass}}", statusClass)
            .Replace("{{traceId}}", x.Id.ToString())

            .Replace("{{time}}", x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{local}}", x.Timestamp.ToLocalTime().ToString("HH:mm:ss"))

            .Replace("{{durationMs}}", x.DurationMs.ToString())
            .Replace("{{durationBadge}}", RenderSlowBadge(TimeSpan.FromMilliseconds(x.DurationMs), options))
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
                string.IsNullOrWhiteSpace(combinedOutgoing)
                    ? "<div class='empty-state trace-empty'>No outgoing dependency calls</div>"
                    : combinedOutgoing)
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

    private static string BuildOutgoingRequestCard(DebugOutgoingRequest request, DebugProbeOptions options)
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
            details: details,
            dataMethod: request.Method,
            dataUrl: request.Url,
            dataHeaders: System.Text.Json.JsonSerializer.Serialize(request.RequestHeaders),
            dataBody: request.RequestBody,
            options: options);
    }

    private static string BuildWaterfallSection(DebugEntry entry, DebugProbeOptions options)
    {
        const double MinPercent = 0.0;
        const double MaxPercent = 100.0;

        if (entry.OutgoingRequests.Count == 0)
        {
            return string.Empty;
        }

        var totalSpan = (double)entry.DurationMs;
        if (totalSpan <= 0)
        {
            totalSpan = 1.0;
        }

        var ticksHtml = new List<string>();
        for (int i = 0; i <= 4; i++)
        {
            var tickVal = (totalSpan * i) / 4.0;
            var tickLeft = i * 25;
            var tickValStr = tickVal.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            ticksHtml.Add($@"<div class=""wf-ruler-tick"" style=""left: {tickLeft}%;"">{tickValStr} ms</div>");
        }

        var rulerHtml = $@"
                <div class=""waterfall-ruler-row"">
                    <div class=""wf-ruler-label-placeholder""></div>
                    <div class=""wf-ruler-ticks"">
                        {string.Join("", ticksHtml)}
                    </div>
                </div>";

        var rowsHtml = new List<string>();

        foreach (var outgoing in entry.OutgoingRequests)
        {
            var startOffsetMs = (outgoing.TimestampUtc - entry.Timestamp.UtcDateTime).TotalMilliseconds - outgoing.DurationMs;

            var left = Math.Clamp((startOffsetMs / totalSpan) * MaxPercent, MinPercent, MaxPercent);
            var width = Math.Clamp(((double)outgoing.DurationMs / totalSpan) * MaxPercent, MinPercent, MaxPercent);

            var leftStr = left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var widthStr = width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            var barClass = "wf-bar";
            if (!outgoing.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(outgoing.Exception))
            {
                barClass += " wf-bar--error";
            }

            var displayLabel = GetDisplayTarget(outgoing.Url);

            var dataStart = Encode(Math.Max(0, startOffsetMs).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            var dataDuration = Encode(outgoing.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var dataUrl = Encode(displayLabel);
            var dataStatus = Encode(outgoing.StatusCode.HasValue ? outgoing.StatusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Failed");

            var badge = RenderSlowBadge(TimeSpan.FromMilliseconds(outgoing.DurationMs), options);
            var badgeHtml = string.IsNullOrEmpty(badge) ? "" : " " + badge;

            rowsHtml.Add($@"
                <div class=""waterfall-row"">
                    <span class=""wf-label"" title=""{Encode(outgoing.Url)}"">{Encode(displayLabel)}</span>
                    <div class=""wf-track"">
                        <div class=""{barClass}"" style=""left: {leftStr}%; width: {widthStr}%;""
                             data-wf-start=""{dataStart}""
                             data-wf-duration=""{dataDuration}""
                             data-wf-url=""{dataUrl}""
                             data-wf-status=""{dataStatus}"">{outgoing.DurationMs} ms{badgeHtml}</div>
                    </div>
                </div>");
        }

        return $@"
        <article class=""trace-card waterfall-container"">
            <div class=""trace-card-main"">
                <div class=""trace-card-header"">
                    <div class=""trace-card-title"">
                        <span class=""trace-dot"" aria-hidden=""true""></span>
                        <span class=""trace-label"">Waterfall Timeline</span>
                    </div>
                </div>
                <div class=""trace-details"">
                    {rulerHtml}
                    {string.Join("", rowsHtml)}
                </div>
            </div>
        </article>";
    }

    private static string BuildTraceCard(
        string label,
        string method,
        string target,
        string classes,
        IEnumerable<string> details,
        int? statusCode = null,
        string? statusText = null,
        long? durationMs = null,
        string? dataMethod = null,
        string? dataUrl = null,
        string? dataHeaders = null,
        string? dataBody = null,
        DebugProbeOptions? options = null)
    {
        options ??= new DebugProbeOptions();
        var targetHost = GetDisplayTarget(target);
        var status = statusCode.HasValue
            ? $@"<span class=""status {GetStatusClass(statusCode.Value)}"">{Encode(GetStatusText(statusCode.Value))}</span>"
            : !string.IsNullOrWhiteSpace(statusText)
                ? $@"<span class=""status status-500"">{Encode(statusText)}</span>"
            : "";

        var durationBadge = durationMs.HasValue ? RenderSlowBadge(TimeSpan.FromMilliseconds(durationMs.Value), options) : "";
        var durationHtml = durationMs.HasValue 
            ? $@"<span>{durationMs.Value} ms{(string.IsNullOrEmpty(durationBadge) ? "" : " " + durationBadge)}</span>" 
            : "";

        var methodPill = !string.IsNullOrWhiteSpace(method)  ? $@"<span class=""method-pill"">{Encode(method)}</span>" : "";

        var dataAttrs = "";
        if (!string.IsNullOrWhiteSpace(dataMethod)) dataAttrs += $" data-method=\"{Encode(dataMethod)}\"";
        if (!string.IsNullOrWhiteSpace(dataUrl)) dataAttrs += $" data-url=\"{Encode(dataUrl)}\"";
        if (!string.IsNullOrWhiteSpace(dataHeaders)) dataAttrs += $" data-headers=\"{Encode(dataHeaders)}\"";
        if (!string.IsNullOrWhiteSpace(dataBody)) dataAttrs += $" data-body=\"{Encode(dataBody)}\"";

        var copyBtns = "";
        if (!string.IsNullOrWhiteSpace(dataMethod))
        {
            copyBtns = $@"
                        <button class=""curl-copy-btn"" 
                                type=""button"" 
                                title=""Copy as cURL"" 
                                aria-label=""Copy as cURL"" 
                                onclick=""copyAsCurl(this)"">
                            <svg viewBox=""0 0 24 24"" aria-hidden=""true"">
                                <path d=""M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2""></path>
                                <rect x=""8"" y=""2"" width=""8"" height=""4"" rx=""1"" ry=""1""></rect>
                            </svg>
                        </button>
                        <button class=""csharp-copy-btn"" 
                                type=""button"" 
                                title=""Copy as C#"" 
                                aria-label=""Copy as C#"" 
                                onclick=""copyAsCSharp(this)"">
                            <svg viewBox=""0 0 24 24"" aria-hidden=""true"">
                                <path d=""M16 18l6-6-6-6""></path>
                                <path d=""M8 6l-6 6 6 6""></path>
                            </svg>
                        </button>
                        <button class=""markdown-copy-btn"" 
                                type=""button"" 
                                title=""Copy as Markdown"" 
                                aria-label=""Copy as Markdown"" 
                                onclick=""copyAsMarkdown(this)"">
                            <svg viewBox=""0 0 24 24"" aria-hidden=""true"">
                                <rect x=""3"" y=""5"" width=""18"" height=""14"" rx=""2"" ry=""2""></rect>
                                <path d=""M7 15V9l2.5 3 2.5-3v6M17 11l-2 2-2-2M15 9v4""></path>
                            </svg>
                        </button>";
        }

        return $@"
        <article class=""trace-card {Encode(classes)}""{dataAttrs}>
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
                        {durationHtml}
                        {copyBtns}
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

    private static string RenderSlowBadge(TimeSpan duration, DebugProbeOptions options)
    {
        if (options.SlowRequestThresholdMs > 0 && duration.TotalMilliseconds >= options.SlowRequestThresholdMs)
        {
            return $@"<span class=""dbp-badge dbp-badge-slow"" title=""Exceeds {options.SlowRequestThresholdMs}ms threshold"">SLOW</span>";
        }
        return string.Empty;
    }

}
