using DebugProbe.AspNetCore.Internal.Rendering;
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
        Assert.Contains("&quot;request&quot;:true", html);
        Assert.Contains("&quot;response&quot;:true", html);
    }

    [Fact]
    public void Payload_badges_render_for_json_empty_text_and_hidden_payloads()
    {
        var jsonHtml = HtmlRenderer.RenderDetailsPage(CreateEntry(), CreateEnvironment(), "{\"ok\":true}", "plain");
        var emptyHtml = HtmlRenderer.RenderDetailsPage(CreateEntry(), CreateEnvironment(), "", "");
        var hiddenHtml = HtmlRenderer.RenderDetailsPage(CreateEntry(), CreateEnvironment(), "[Body too large]", "[Body too large]");

        Assert.Contains("payload-json", jsonHtml);
        Assert.Contains("payload-text", jsonHtml);
        Assert.Contains("payload-empty", emptyHtml);
        Assert.Contains("payload-hidden", hiddenHtml);
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
