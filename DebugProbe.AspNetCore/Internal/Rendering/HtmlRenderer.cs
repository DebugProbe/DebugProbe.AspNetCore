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

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var queryIndex = path.IndexOf('?');
        var normalized = queryIndex >= 0 ? path[..queryIndex] : path;

        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    public static string RenderIndexPage(List<DebugEntry> items, DebugProbeOptions? options = null)
    {
        options ??= new DebugProbeOptions();
        var slowRequestThresholdMs = options.SlowRequestThresholdMs;
        var store = DebugEntryStore.Instance;
        var prefix = options.RoutePrefix;

        var routesWithDiffs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routeDiffTooltips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (options.AutoEnvironmentDiff && items.Count > 0)
        {
            var groups = new Dictionary<string, Dictionary<string, DebugEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in items)
            {
                var method = entry.Method ?? string.Empty;
                var normalizedPath = NormalizePath(entry.Path);
                var groupKey = $"{method}:{normalizedPath}";
                var env = store?.GetEnvironment(entry)?.Environment ?? "Unknown";

                if (!groups.TryGetValue(groupKey, out var envDict))
                {
                    envDict = new Dictionary<string, DebugEntry>(StringComparer.OrdinalIgnoreCase);
                    groups[groupKey] = envDict;
                }

                if (!envDict.ContainsKey(env))
                {
                    envDict[env] = entry;
                }
            }

            foreach (var kvp in groups)
            {
                var envDict = kvp.Value;
                if (envDict.Count >= 2)
                {
                    var topTwo = envDict.Values.OrderByDescending(e => e.Timestamp).Take(2).ToList();
                    var diffs = DebugProbe.AspNetCore.Internal.Compare.DebugEntryComparer.Compare(topTwo[0], topTwo[1]);
                    if (diffs != null && diffs.Count > 0)
                    {
                        routesWithDiffs.Add(kvp.Key);
                        var envNames = topTwo.Select(e => store?.GetEnvironment(e)?.Environment ?? "Unknown").Distinct();
                        routeDiffTooltips[kvp.Key] = string.Join(", ", envNames);
                    }
                }
            }
        }

        // Build pinned-entries section (shown above the normal table)
        var pinnedItems = items.Where(x => x.IsPinned).ToList();
        var pinnedSectionHtml = "";
        if (pinnedItems.Count > 0)
        {
            var pinnedRows = string.Join("", pinnedItems.Select(x =>
            {
                var pathWithQuery = string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}";
                var badge = RenderSlowBadge(TimeSpan.FromMilliseconds(x.DurationMs), options);
                var badgeHtml = string.IsNullOrEmpty(badge) ? "" : " " + badge;
                return $@"<tr data-url=""{Encode(prefix)}/{Encode(x.Id)}"" data-method=""{Encode(x.Method)}"" data-status-family=""{x.StatusCode / 100}"" data-search=""{Encode($"{x.Id} {x.Method} {x.Path} {x.Query} {x.StatusCode}")}"" class=""clickable-row pinned-row""><td>{x.Timestamp:HH:mm:ss}</td><td><span class=""method-pill"">{Encode(x.Method)}</span></td><td class=""request-path""><span class=""request-path-value"" title=""{Encode(pathWithQuery)}"">{Encode(pathWithQuery)}</span></td><td><span class=""status {GetStatusClass(x.StatusCode)}"">{x.StatusCode}</span></td><td>{x.DurationMs} ms{badgeHtml}</td><td><button class=""pin-btn pin-btn--active"" type=""button"" title=""Unpin this trace"" aria-label=""Unpin this trace"" onclick=""event.stopPropagation(); togglePin('{Encode(x.Id)}', '{Encode(prefix)}')"" >📌 Pinned</button></td></tr>";
            }));

            pinnedSectionHtml = $@"
        <h3 style=""display:flex;align-items:center;gap:8px;margin-top:0;"">📌 Pinned Traces <span class=""dbp-badge dbp-badge-pinned"">{pinnedItems.Count}</span></h3>
        <div class=""table-wrap"" style=""margin-bottom:20px;border-color:#6c5ce7;"">
            <table style=""table-layout:fixed;"">
                <thead>
                    <tr>
                        <th>Time</th>
                        <th>Method</th>
                        <th>Path</th>
                        <th>Status</th>
                        <th>Duration</th>
                        <th style=""width:110px;"">Pin</th>
                    </tr>
                </thead>
                <tbody>{pinnedRows}</tbody>
            </table>
        </div>";
        }

        var rows = string.Join("", items.Where(x => !x.IsPinned).Select(x =>
        {
            var pathWithQuery = string.IsNullOrEmpty(x.Query) ? x.Path : $"{x.Path}{x.Query}";
            var badge = RenderSlowBadge(TimeSpan.FromMilliseconds(x.DurationMs), options);
            var badgeHtml = string.IsNullOrEmpty(badge) ? "" : " " + badge;

            var method = x.Method ?? string.Empty;
            var normalizedPath = NormalizePath(x.Path);
            var groupKey = $"{method}:{normalizedPath}";
            var envDiffBadgeHtml = "";

            if (options.AutoEnvironmentDiff && routesWithDiffs.Contains(groupKey))
            {
                if (routeDiffTooltips.TryGetValue(groupKey, out var envList))
                {
                    envDiffBadgeHtml = $@" <span class=""dbp-badge dbp-badge-envdiff"" title=""Payload differences detected between: {Encode(envList)}"">⚠ Env diff</span>";
                }
            }

            return $@"<tr data-url=""{Encode(prefix)}/{Encode(x.Id)}"" data-method=""{Encode(x.Method)}"" data-status-family=""{x.StatusCode / 100}"" data-search=""{Encode($"{x.Id} {x.Method} {x.Path} {x.Query} {x.StatusCode}")}"" class=""clickable-row""><td>{x.Timestamp:HH:mm:ss}</td><td><span class=""method-pill"">{Encode(x.Method)}</span></td><td class=""request-path""><span class=""request-path-value"" title=""{Encode(pathWithQuery)}"">{Encode(pathWithQuery)}</span>{envDiffBadgeHtml}</td><td><span class=""status {GetStatusClass(x.StatusCode)}"">{x.StatusCode}</span></td><td>{x.DurationMs} ms{badgeHtml}</td><td><button class=""pin-btn"" type=""button"" title=""Pin this trace"" aria-label=""Pin this trace"" onclick=""event.stopPropagation(); togglePin('{Encode(x.Id)}', '{Encode(prefix)}')"" >☆ Pin</button></td></tr>";
        }));

        if (string.IsNullOrEmpty(rows))
            rows = "<tr class='empty-row'><td colspan='6'>No data</td></tr>";

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

        // Trend calculations
        store ??= DebugEntryStore.Instance;
        var now = DateTimeOffset.UtcNow;
        var limitTime = now.AddMinutes(-options.TrendLookbackMinutes);
        var midTime = now.AddMinutes(-options.TrendLookbackMinutes / 2.0);

        int[] buckets = new int[options.TrendLookbackMinutes];
        int totalA = 0;
        int errorsA = 0;
        int totalB = 0;
        int errorsB = 0;

        foreach (var entry in items)
        {
            var t = entry.Timestamp;
            var elapsed = now - t;
            if (elapsed.TotalMinutes < options.TrendLookbackMinutes)
            {
                double mins = Math.Max(0.0, elapsed.TotalMinutes);
                int bucketIndex = options.TrendLookbackMinutes - 1 - (int)Math.Floor(mins);
                if (bucketIndex >= 0 && bucketIndex < options.TrendLookbackMinutes)
                {
                    buckets[bucketIndex]++;
                }
            }

            if (t >= midTime)
            {
                totalA++;
                if (entry.StatusCode >= 400)
                {
                    errorsA++;
                }
            }
            else if (t >= limitTime && t < midTime)
            {
                totalB++;
                if (entry.StatusCode >= 400)
                {
                    errorsB++;
                }
            }
        }

        int maxVal = buckets.Max();
        var pointsList = new List<string>();
        for (int i = 0; i < options.TrendLookbackMinutes; i++)
        {
            double x = (double)i / (options.TrendLookbackMinutes - 1) * 120.0;
            double y = maxVal == 0 ? 14.0 : 26.0 - ((double)buckets[i] / maxVal * 24.0);
            pointsList.Add($"{x:0.##},{y:0.##}");
        }
        string pointsString = string.Join(" ", pointsList);

        string sparklineSvg = $@"<svg width=""120"" height=""28"" viewBox=""0 0 120 28"" style=""overflow: visible;"" xmlns=""http://www.w3.org/2000/svg""><polyline fill=""none"" stroke=""#6c5ce7"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" points=""{pointsString}"" /></svg>";

        double errorRateA = totalA == 0 ? 0.0 : (double)errorsA / totalA;
        double errorRateB = totalB == 0 ? 0.0 : (double)errorsB / totalB;

        string trendArrow;
        string arrowClass;
        if (totalA == 0 || totalB == 0)
        {
            trendArrow = "→";
            arrowClass = "trend-neutral";
        }
        else if (errorRateA > errorRateB)
        {
            trendArrow = "↑";
            arrowClass = "trend-up";
        }
        else if (errorRateA < errorRateB)
        {
            trendArrow = "↓";
            arrowClass = "trend-down";
        }
        else
        {
            trendArrow = "→";
            arrowClass = "trend-neutral";
        }

        string errorTrendHtml = $" <span class=\"trend-arrow {arrowClass}\" title=\"vs preceding period\">{trendArrow}</span>";

        var exceptionPanel = "";
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

        // Insert pinned section before the exception panel and before the main table.
        // Both are injected before the first <div class="table-wrap"> in the template.
        var insertionHtml = pinnedSectionHtml + (string.IsNullOrEmpty(exceptionPanel) ? "" : exceptionPanel);
        if (!string.IsNullOrEmpty(insertionHtml))
        {
            var idx = pageHtml.IndexOf("<div class=\"table-wrap\">");
            if (idx >= 0)
            {
                pageHtml = pageHtml.Insert(idx, insertionHtml);
            }
        }

        // The unpinned count is what the visible table shows.
        var unpinnedCount = items.Count(x => !x.IsPinned);

        return BuildLayout(pageHtml
            .Replace("{{rows}}", rows)
            .Replace("{{total_count}}", unpinnedCount.ToString())
            .Replace("{{method_options}}", methodOptions)
            .Replace("{{total_requests}}", FormatCompactNumber(totalRequests))
            .Replace("{{avg_response_time}}", $"{averageResponseMs} ms")
            .Replace("{{slow_requests}}", FormatCompactNumber(slowRequests))
            .Replace("{{error_rate}}", $"{errorRate:0.#}%")
            .Replace("{{error_trend}}", errorTrendHtml)
            .Replace("{{sparkline}}", sparklineSvg));
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

        // Gate 1: config. Gate 2: env. Both must be true for the preview to render.
        // Enforced server-side here — there is no client-side CSS/JS gating.
        var isDevEnv = string.Equals(e.Environment, "Development", StringComparison.OrdinalIgnoreCase);
        if (options.AllowRedactionPreview && isDevEnv)
        {
            var previewBanner = BuildRedactionPreviewBanner(x);
            // Insert the banner just before the first <section class="trace-flow"
            const string insertionMarker = "<section class=\"trace-flow\"";
            var insertAt = content.IndexOf(insertionMarker, StringComparison.Ordinal);
            if (insertAt >= 0)
            {
                content = content.Insert(insertAt, previewBanner);
            }
        }

        return BuildLayout(content);
    }

    private static string BuildRedactionPreviewBanner(DebugEntry x)
    {
        var hasOriginals = x.OriginalRequestHeaders.Count > 0
            || !string.IsNullOrWhiteSpace(x.OriginalRequestBody)
            || !string.IsNullOrWhiteSpace(x.OriginalResponseBody)
            || !string.IsNullOrWhiteSpace(x.OriginalQuery);

        if (!hasOriginals)
        {
            return string.Empty;
        }

        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(x.OriginalQuery) && x.OriginalQuery != x.Query)
        {
            sections.Add(BuildOriginalValueSection("Original Query", x.OriginalQuery));
        }

        var diffHeaders = x.OriginalRequestHeaders
            .Where(kv => !x.RequestHeaders.TryGetValue(kv.Key, out var redacted)
                         || redacted != kv.Value)
            .ToList();
        if (diffHeaders.Count > 0)
        {
            var rows = string.Join("", diffHeaders.Select(kv =>
                $@"<div class=""header-row""><span>{Encode(kv.Key)}</span><code class=""redaction-original-value"">{Encode(kv.Value)}</code></div>"));
            sections.Add($@"
        <details class=""payload-panel"">
            <summary><span>Original Request Headers (with secrets)</span><small>{diffHeaders.Count} revealed</small></summary>
            <div class=""headers-grid"">{rows}</div>
        </details>");
        }

        if (!string.IsNullOrWhiteSpace(x.OriginalRequestBody) && x.OriginalRequestBody != x.RequestBody)
        {
            sections.Add(BuildOriginalValueSection("Original Request Body (with secrets)", x.OriginalRequestBody));
        }

        if (!string.IsNullOrWhiteSpace(x.OriginalResponseBody) && x.OriginalResponseBody != x.ResponseBody)
        {
            sections.Add(BuildOriginalValueSection("Original Response Body (with secrets)", x.OriginalResponseBody));
        }

        if (sections.Count == 0)
        {
            return string.Empty;
        }

        return $@"
        <details class=""trace-card"" style=""margin-bottom:8px;"">
            <summary style=""display:flex;align-items:center;gap:10px;padding:10px 12px;background:#fff;border:1px solid rgba(231,76,60,0.25);border-radius:8px;cursor:pointer;font-size:13px;font-weight:700;color:#b42318;list-style:none;"">
                <span style=""color:#b42318;"">⚠</span>
                <span>Redaction Preview — local only</span>
                <small style=""font-weight:400;color:#6b7280;"">Showing pre-redaction values (AllowRedactionPreview=true, Development)</small>
            </summary>
            <div class=""trace-card-main"" style=""border-color:rgba(231,76,60,0.25);"">
                <div class=""trace-details"">
                    {string.Join("", sections)}
                </div>
            </div>
        </details>";
    }

    private static string BuildOriginalValueSection(string title, string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : JsonUtils.Format(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return $@"
        <details class=""payload-panel"">
            <summary><span>{Encode(title)}</span><small>original - {FormatBytes(text.Length)}</small></summary>
            <div class=""code-block"">
                <button class=""copy-btn"" type=""button"" onclick=""copyText(this)"">Copy</button>
                <pre class=""redaction-original-value"">{Encode(text)}</pre>
            </div>
        </details>";
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

    private static string RenderPinnedBadge()
    {
        return @"<span class=""dbp-badge dbp-badge-pinned"">📌 Pinned</span>";
    }

}
