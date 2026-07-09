using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Internal.Resources;
using DebugProbe.AspNetCore.Models;

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
}
