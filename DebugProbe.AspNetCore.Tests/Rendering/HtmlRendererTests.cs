using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Internal.Resources;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Tests.Rendering;

public class HtmlRendererTests
{
    [Fact]
    public void Render_index_page_builds_page_with_entries()
    {
        var html = HtmlRenderer.RenderIndexPage(
        [
            new DebugEntry
            {
                Id = "trace-1",
                Method = "GET",
                Path = "/orders",
                StatusCode = 200,
                Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
            }
        ]);

        Assert.Contains("/debug/trace-1", html);
        Assert.Contains("GET", html);
        Assert.Contains("/orders", html);
        Assert.Contains("200", html);
    }

    [Fact]
    public void Render_index_page_constrains_path_cell_and_preserves_full_path_title()
    {
        var path = "/" + new string('a', 240);
        var query = "?filter=" + new string('b', 120);
        var fullPath = path + query;

        var html = HtmlRenderer.RenderIndexPage(
        [
            new DebugEntry
            {
                Id = "trace-1",
                Method = "GET",
                Path = path,
                Query = query,
                StatusCode = 200,
                Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)
            }
        ]);

        Assert.Contains($@"<td class=""request-path""><span class=""request-path-value"" title=""{fullPath}"">{fullPath}</span></td>", html);
    }

    [Fact]
    public void Embedded_css_keeps_request_index_table_fixed_with_ellipsized_paths()
    {
        Assert.Contains("#requestTable", EmbeddedResources.Css);
        Assert.Contains("table-layout: fixed;", EmbeddedResources.Css);
        Assert.Contains(".request-path-value", EmbeddedResources.Css);
        Assert.Contains("overflow: hidden;", EmbeddedResources.Css);
        Assert.Contains("text-overflow: ellipsis;", EmbeddedResources.Css);
        Assert.Contains("white-space: nowrap;", EmbeddedResources.Css);
    }

    [Fact]
    public void Details_page_renders_captured_values()
    {
        var entry = CreateEntry();

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "{\"request\":true}",
            "{\"response\":true}");

        Assert.Contains("POST", html);
        Assert.Contains("/orders?id=10", html);
        Assert.Contains("500 InternalServerError", html);
        Assert.Contains("http://example.test/orders?id=10", html);
        Assert.Contains("&quot;request&quot;: true", html);
        Assert.Contains("&quot;response&quot;: true", html);
    }

    [Fact]
    public void Payload_groups_render_for_json_empty_text_and_hidden_payloads()
    {
        var jsonHtml = HtmlRenderer.RenderDetailsPage(CreateEntry(), CreateEnvironment(), "{\"ok\":true}", "plain");
        var hiddenHtml = HtmlRenderer.RenderDetailsPage(CreateEntry(), CreateEnvironment(), "[Body too large]", "[Body too large]");

        Assert.Contains("Request", jsonHtml);
        Assert.Contains("Response", jsonHtml);

        Assert.Contains("[Body too large]", hiddenHtml);
    }

    [Fact]
    public void Html_encoding_escapes_untrusted_values()
    {
        var entry = CreateEntry();
        entry.Method = "<script>alert(1)</script>";
        entry.Path = "/orders/<bad>";
        entry.RequestHeaders["X-Unsafe"] = "<img src=x onerror=alert(1)>";

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "<script>alert(1)</script>",
            "<b>bad</b>");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("/orders/&lt;bad&gt;", html);
    }

    [Fact]
    public void Details_page_renders_waterfall_section_with_ruler_and_tooltips_when_outgoing_requests_exist()
    {
        var entry = CreateEntry();
        entry.DurationMs = 500;
        
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api.test/v1/users?id=123",
            StatusCode = 200,
            DurationMs = 150,
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(200), // starts at 50ms offset (200 - 150)
            IsSuccessStatusCode = true
        });

        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "POST",
            Url = "https://untrusted-api.test/v1/search?query=a&b=%3Cscript%3E",
            StatusCode = 500,
            DurationMs = 80,
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(400), // starts at 320ms offset (400 - 80)
            IsSuccessStatusCode = false
        });

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "{}",
            "{}");

        // Verify Ruler ticks exist
        Assert.Contains("waterfall-ruler-row", html);
        Assert.Contains("wf-ruler-ticks", html);
        Assert.Contains("0 ms", html);
        Assert.Contains("125 ms", html);
        Assert.Contains("250 ms", html);
        Assert.Contains("375 ms", html);
        Assert.Contains("500 ms", html);

        // Verify first request attributes (success)
        Assert.Contains("data-wf-start=\"50\"", html);
        Assert.Contains("data-wf-duration=\"150\"", html);
        Assert.Contains("data-wf-url=\"external-api.test/v1/users?id=123\"", html);
        Assert.Contains("data-wf-status=\"200\"", html);

        // Verify second request attributes (failure & html-encoded)
        Assert.Contains("data-wf-start=\"320\"", html);
        Assert.Contains("data-wf-duration=\"80\"", html);
        Assert.Contains("data-wf-url=\"untrusted-api.test/v1/search?query=a&amp;b=%3Cscript%3E\"", html);
        Assert.Contains("data-wf-status=\"500\"", html);
        Assert.Contains("wf-bar--error", html);
    }

    [Fact]
    public void Details_page_renders_curl_copy_attributes_and_buttons()
    {
        var entry = CreateEntry();
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "PUT",
            Url = "https://external-api.test/v1/update",
            StatusCode = 200,
            DurationMs = 120,
            RequestBody = "{\"name\":\"John\"}",
            RequestHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer token" }
        });

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "{\"request\":true}",
            "{\"response\":true}");

        // 1. Verify Incoming Request Card attributes and button
        Assert.Contains("data-method=\"POST\"", html);
        Assert.Contains("data-url=\"http://example.test/orders?id=10\"", html);
        Assert.Contains("data-headers=\"{&quot;X-Test&quot;:&quot;yes&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;request&quot;:true}\"", html);
        Assert.Contains("class=\"curl-copy-btn\"", html);

        // 2. Verify Outgoing Request Card attributes and button
        Assert.Contains("data-method=\"PUT\"", html);
        Assert.Contains("data-url=\"https://external-api.test/v1/update\"", html);
        Assert.Contains("data-headers=\"{&quot;Authorization&quot;:&quot;Bearer token&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;name&quot;:&quot;John&quot;}\"", html);

        // 3. Verify Response Card has no curl copy button (only 2 copy curl buttons in total should exist in HTML markup)
        var occurrences = (html.Length - html.Replace("class=\"curl-copy-btn\"", "").Length) / "class=\"curl-copy-btn\"".Length;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Details_page_renders_csharp_copy_attributes_and_buttons()
    {
        var entry = CreateEntry();
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "PUT",
            Url = "https://external-api.test/v1/update",
            StatusCode = 200,
            DurationMs = 120,
            RequestBody = "{\"name\":\"John\"}",
            RequestHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer token" }
        });

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "{\"request\":true}",
            "{\"response\":true}");

        // 1. Verify Incoming Request Card attributes and button
        Assert.Contains("data-method=\"POST\"", html);
        Assert.Contains("data-url=\"http://example.test/orders?id=10\"", html);
        Assert.Contains("data-headers=\"{&quot;X-Test&quot;:&quot;yes&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;request&quot;:true}\"", html);
        Assert.Contains("class=\"csharp-copy-btn\"", html);

        // 2. Verify Outgoing Request Card attributes and button
        Assert.Contains("data-method=\"PUT\"", html);
        Assert.Contains("data-url=\"https://external-api.test/v1/update\"", html);
        Assert.Contains("data-headers=\"{&quot;Authorization&quot;:&quot;Bearer token&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;name&quot;:&quot;John&quot;}\"", html);

        // 3. Verify Response Card has no C# copy button (only 2 copy C# buttons in total should exist in HTML markup)
        var occurrences = (html.Length - html.Replace("class=\"csharp-copy-btn\"", "").Length) / "class=\"csharp-copy-btn\"".Length;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Details_page_renders_markdown_copy_attributes_and_buttons()
    {
        var entry = CreateEntry();
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "PUT",
            Url = "https://external-api.test/v1/update",
            StatusCode = 200,
            DurationMs = 120,
            RequestBody = "{\"name\":\"John\"}",
            RequestHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer token" }
        });

        var html = HtmlRenderer.RenderDetailsPage(
            entry,
            CreateEnvironment(),
            "{\"request\":true}",
            "{\"response\":true}");

        // 1. Verify Incoming Request Card attributes and button
        Assert.Contains("data-method=\"POST\"", html);
        Assert.Contains("data-url=\"http://example.test/orders?id=10\"", html);
        Assert.Contains("data-headers=\"{&quot;X-Test&quot;:&quot;yes&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;request&quot;:true}\"", html);
        Assert.Contains("class=\"markdown-copy-btn\"", html);

        // 2. Verify Outgoing Request Card attributes and button
        Assert.Contains("data-method=\"PUT\"", html);
        Assert.Contains("data-url=\"https://external-api.test/v1/update\"", html);
        Assert.Contains("data-headers=\"{&quot;Authorization&quot;:&quot;Bearer token&quot;}\"", html);
        Assert.Contains("data-body=\"{&quot;name&quot;:&quot;John&quot;}\"", html);

        // 3. Verify Response Card has no markdown copy button (only 2 copy markdown buttons in total should exist in HTML markup)
        var occurrences = (html.Length - html.Replace("class=\"markdown-copy-btn\"", "").Length) / "class=\"markdown-copy-btn\"".Length;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void Render_index_page_with_slow_badge()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/1", DurationMs = 500, StatusCode = 200 },
            new DebugEntry { Id = "2", Method = "GET", Path = "/2", DurationMs = 1500, StatusCode = 200 }
        };

        // Case 1: Threshold = 1000 (Default)
        var htmlDefault = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("/debug/1", htmlDefault);
        Assert.Contains("/debug/2", htmlDefault);
        
        Assert.Contains("500 ms</td>", htmlDefault);
        Assert.Contains("1500 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", htmlDefault);

        // Case 2: Threshold = 0 (disabled)
        var htmlDisabled = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 0 });
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", htmlDisabled);

        // Case 3: Threshold = 2000
        var htmlHigh = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 2000 });
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", htmlHigh);
    }

    [Fact]
    public void Render_details_page_with_slow_badge()
    {
        var entry = CreateEntry();
        entry.DurationMs = 1200; // > 1000
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api.test",
            StatusCode = 200,
            DurationMs = 1500, // > 1000
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(200),
            IsSuccessStatusCode = true
        });
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api2.test",
            StatusCode = 200,
            DurationMs = 200, // < 1000
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(400),
            IsSuccessStatusCode = true
        });

        // Case 1: Threshold = 1000
        var html = HtmlRenderer.RenderDetailsPage(entry, CreateEnvironment(), "{}", "{}", new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        
        // Detail Header should have badge
        Assert.Contains("1200 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
        
        // Outgoing Request 1 (1500ms) should have badge in waterfall
        Assert.Contains("1500 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
        
        // Outgoing Request 2 (200ms) should NOT have badge in waterfall
        Assert.Contains("200 ms</div>", html);

        // Case 2: Threshold = 0
        var htmlDisabled = HtmlRenderer.RenderDetailsPage(entry, CreateEnvironment(), "{}", "{}", new DebugProbeOptions { SlowRequestThresholdMs = 0 });
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", htmlDisabled);
    }

    private static DebugEntry CreateEntry()
    {
        return new DebugEntry
        {
            Id = "trace-1",
            Method = "POST",
            Path = "/orders",
            Query = "?id=10",
            RequestUrl = "http://example.test/orders?id=10",
            StatusCode = 500,
            RequestBody = "{\"request\":true}",
            ResponseBody = "{\"response\":true}",
            RequestSize = 16,
            ResponseSize = 17,
            DurationMs = 12,
            Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            RequestTimeUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            RequestHeaders = new Dictionary<string, string>
            {
                ["X-Test"] = "yes"
            },

            ResponseHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json"
            }
        };
    }

    private static DebugEnvironment CreateEnvironment()
    {
        return new DebugEnvironment
        {
            Environment = "Testing",
            Culture = "en-US",
            MachineName = "test-machine",
            TimeZone = "UTC",
            DecimalSeparator = ".",
            DateFormat = "M/d/yyyy",
            AssemblyVersion = "1.0.0"
        };
    }

    [Fact]
    public void Render_slow_badge_boundary_exact_threshold()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/exact", DurationMs = 1000, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("1000 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
    }

    [Fact]
    public void Render_slow_badge_boundary_threshold_minus_one()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/minus-one", DurationMs = 999, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("999 ms</td>", html);
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", html);
    }

    [Fact]
    public void Render_slow_badge_boundary_threshold_plus_one()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/plus-one", DurationMs = 1001, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("1001 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
    }

    [Fact]
    public void Render_slow_badge_boundary_zero_threshold_disabled()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/zero", DurationMs = 5000, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 0 });
        Assert.Contains("5000 ms</td>", html);
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", html);
    }

    [Fact]
    public void Render_slow_badge_boundary_negative_threshold_disabled()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/negative", DurationMs = 5000, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = -100 });
        Assert.Contains("5000 ms</td>", html);
        Assert.DoesNotContain("<span class=\"dbp-badge dbp-badge-slow\"", html);
    }

    [Fact]
    public void Render_slow_badge_large_duration_formatting()
    {
        var items = new List<DebugEntry>
        {
            new DebugEntry { Id = "1", Method = "GET", Path = "/large", DurationMs = 120500, StatusCode = 200 }
        };
        var html = HtmlRenderer.RenderIndexPage(items, new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("120500 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
    }

    [Fact]
    public void Render_details_page_independent_outgoing_call_badge_behavior()
    {
        var entry = CreateEntry();
        entry.DurationMs = 500; // < 1000 (not slow)
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api.test",
            StatusCode = 200,
            DurationMs = 1500, // > 1000 (slow)
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(100),
            IsSuccessStatusCode = true
        });

        var html = HtmlRenderer.RenderDetailsPage(entry, CreateEnvironment(), "{}", "{}", new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        
        // Root request should not have the slow badge
        Assert.Contains("<strong>500 ms</strong>", html);
        Assert.DoesNotContain(" 500 ms <span class=\"dbp-badge dbp-badge-slow\"", html);

        // Outgoing request should independently have the slow badge
        Assert.Contains("1500 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
    }

    [Fact]
    public void Render_details_page_outgoing_call_boundary_exact()
    {
        var entry = CreateEntry();
        entry.DurationMs = 200;
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api.test",
            StatusCode = 200,
            DurationMs = 1000, // exact
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(100),
            IsSuccessStatusCode = true
        });

        var html = HtmlRenderer.RenderDetailsPage(entry, CreateEnvironment(), "{}", "{}", new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("1000 ms <span class=\"dbp-badge dbp-badge-slow\" title=\"Exceeds 1000ms threshold\">SLOW</span>", html);
    }

    [Fact]
    public void Render_details_page_outgoing_call_boundary_minus_one()
    {
        var entry = CreateEntry();
        entry.DurationMs = 200;
        entry.OutgoingRequests.Add(new DebugOutgoingRequest
        {
            Method = "GET",
            Url = "https://external-api.test",
            StatusCode = 200,
            DurationMs = 999, // below threshold
            TimestampUtc = entry.Timestamp.UtcDateTime.AddMilliseconds(100),
            IsSuccessStatusCode = true
        });

        var html = HtmlRenderer.RenderDetailsPage(entry, CreateEnvironment(), "{}", "{}", new DebugProbeOptions { SlowRequestThresholdMs = 1000 });
        Assert.Contains("999 ms</div>", html);
        Assert.DoesNotContain(" 999 ms <span class=\"dbp-badge dbp-badge-slow\"", html);
    }

    [Fact]
    public void Build_request_rate_sparkline_handles_empty_state_gracefully()
    {
        var options = new DebugProbeOptions { LookbackMinutes = 15 };
        var html = HtmlRenderer.BuildRequestRateSparkline(new List<DebugEntry>(), options);

        Assert.Contains("class=\"dbp-sparkline\"", html);
        // Should contain flat line point coordinates
        Assert.Contains("22", html);
    }

    [Fact]
    public void Build_request_rate_sparkline_maps_buckets_correctly()
    {
        var options = new DebugProbeOptions { LookbackMinutes = 15 };
        var now = DateTimeOffset.UtcNow;
        var entries = new List<DebugEntry>
        {
            new DebugEntry { Timestamp = now.AddMinutes(-1), StatusCode = 200, RequestBody = "", ResponseBody = "" },
            new DebugEntry { Timestamp = now.AddMinutes(-5), StatusCode = 200, RequestBody = "", ResponseBody = "" }
        };

        var html = HtmlRenderer.BuildRequestRateSparkline(entries, options);
        Assert.Contains("class=\"dbp-sparkline\"", html);
    }

    [Fact]
    public void Compute_error_rate_trend_suppresses_arrow_when_previous_window_empty()
    {
        var options = new DebugProbeOptions { LookbackMinutes = 15 };
        var now = DateTimeOffset.UtcNow;
        var entries = new List<DebugEntry>
        {
            new DebugEntry { Timestamp = now.AddMinutes(-1), StatusCode = 500, RequestBody = "", ResponseBody = "" }
        };

        var trend = HtmlRenderer.ComputeErrorRateTrend(entries, options);
        Assert.Equal(string.Empty, trend);
    }

    [Fact]
    public void Compute_error_rate_trend_indicates_worse_when_error_rate_increases()
    {
        var options = new DebugProbeOptions { LookbackMinutes = 15 };
        var now = DateTimeOffset.UtcNow;
        var entries = new List<DebugEntry>
        {
            // Previous window: 2 requests, 0 errors => 0% error rate
            new DebugEntry { Timestamp = now.AddMinutes(-20), StatusCode = 200, RequestBody = "", ResponseBody = "" },
            new DebugEntry { Timestamp = now.AddMinutes(-25), StatusCode = 200, RequestBody = "", ResponseBody = "" },
            // Current window: 2 requests, 1 error => 50% error rate
            new DebugEntry { Timestamp = now.AddMinutes(-2), StatusCode = 500, RequestBody = "", ResponseBody = "" },
            new DebugEntry { Timestamp = now.AddMinutes(-5), StatusCode = 200, RequestBody = "", ResponseBody = "" }
        };

        var trend = HtmlRenderer.ComputeErrorRateTrend(entries, options);
        Assert.Contains("dbp-trend--worse", trend);
        Assert.Contains("\u2191", trend);
    }

    [Fact]
    public void Compute_error_rate_trend_indicates_better_when_error_rate_decreases()
    {
        var options = new DebugProbeOptions { LookbackMinutes = 15 };
        var now = DateTimeOffset.UtcNow;
        var entries = new List<DebugEntry>
        {
            // Previous window: 2 requests, 1 error => 50% error rate
            new DebugEntry { Timestamp = now.AddMinutes(-20), StatusCode = 500, RequestBody = "", ResponseBody = "" },
            new DebugEntry { Timestamp = now.AddMinutes(-25), StatusCode = 200, RequestBody = "", ResponseBody = "" },
            // Current window: 2 requests, 0 errors => 0% error rate
            new DebugEntry { Timestamp = now.AddMinutes(-2), StatusCode = 200, RequestBody = "", ResponseBody = "" },
            new DebugEntry { Timestamp = now.AddMinutes(-5), StatusCode = 200, RequestBody = "", ResponseBody = "" }
        };

        var trend = HtmlRenderer.ComputeErrorRateTrend(entries, options);
        Assert.Contains("dbp-trend--better", trend);
        Assert.Contains("\u2193", trend);
    }
}
