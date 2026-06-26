using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DebugProbe.AspNetCore.Handlers;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Tests.Handlers;

public class DebugProbeHttpClientHandlerBodyTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a handler wired to the given DebugEntry and options, with a stub
    /// inner handler that echoes <paramref name="responseContent"/> back.
    /// </summary>
    private static (HttpClient client, DebugEntry entry) BuildClient(
        DebugProbeOptions options,
        HttpContent? responseContent = null)
    {
        var entry = new DebugEntry();
        var context = new DefaultHttpContext();
        context.Items["DebugProbeEntry"] = entry;

        var handler = new DebugProbeHttpClientHandler(
            new HttpContextAccessor { HttpContext = context },
            options)
        {
            InnerHandler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = responseContent
                })
        };

        return (new HttpClient(handler), entry);
    }

    // ---------------------------------------------------------------------------
    // Test cases
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Body_under_limit_is_captured_without_truncation_marker()
    {
        const int limitKb = 1;                    // 1 KB limit
        var bodyText = new string('A', 100);      // 100 bytes — well under 1 KB

        var options = new DebugProbeOptions { MaxBodyCaptureSizeKb = limitKb };
        var (client, entry) = BuildClient(options,
            responseContent: new StringContent(bodyText, Encoding.UTF8, "text/plain"));

        await client.GetAsync("https://example.test/");

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.DoesNotContain("[truncated]", outgoing.ResponseBody);
        Assert.Contains(bodyText, outgoing.ResponseBody);
    }

    [Fact]
    public async Task Body_exactly_at_limit_is_captured_without_truncation_marker()
    {
        const int limitKb = 1;
        var bodyText = new string('B', 1024);     // exactly 1 KB

        var options = new DebugProbeOptions { MaxBodyCaptureSizeKb = limitKb };
        var (client, entry) = BuildClient(options,
            responseContent: new StringContent(bodyText, Encoding.UTF8, "text/plain"));

        await client.GetAsync("https://example.test/");

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.DoesNotContain("[truncated]", outgoing.ResponseBody);
        Assert.Contains(bodyText, outgoing.ResponseBody);
    }

    [Fact]
    public async Task Body_over_limit_is_truncated_and_marker_is_appended()
    {
        const int limitKb = 1;
        var bodyText = new string('C', 2048);     // 2 KB — double the limit

        var options = new DebugProbeOptions { MaxBodyCaptureSizeKb = limitKb };
        var (client, entry) = BuildClient(options,
            responseContent: new StringContent(bodyText, Encoding.UTF8, "text/plain"));

        await client.GetAsync("https://example.test/");

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.EndsWith("[truncated]", outgoing.ResponseBody);
        // Captured prefix must be exactly the limit (1024 'C' chars)
        Assert.StartsWith(new string('C', 1024), outgoing.ResponseBody);
    }

    [Fact]
    public async Task Capturing_streamed_response_body_does_not_consume_body_for_caller()
    {
        const int limitKb = 1;
        var bodyText = new string('D', 2048);
        var responseContent = new StreamContent(new NonSeekableMemoryStream(Encoding.UTF8.GetBytes(bodyText)));
        responseContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
        {
            CharSet = Encoding.UTF8.WebName
        };

        var options = new DebugProbeOptions { MaxBodyCaptureSizeKb = limitKb };
        var (client, entry) = BuildClient(options, responseContent);

        using var response = await client.GetAsync("https://example.test/");

        Assert.Equal(bodyText, await response.Content.ReadAsStringAsync());

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.EndsWith("[truncated]", outgoing.ResponseBody);
    }

    [Fact]
    public async Task Captured_body_uses_content_type_charset()
    {
        const string bodyText = "Hello \u0100";
        var responseContent = new StringContent(bodyText, Encoding.Unicode, "text/plain");

        var options = new DebugProbeOptions { MaxBodyCaptureSizeKb = 1 };
        var (client, entry) = BuildClient(options, responseContent);

        using var response = await client.GetAsync("https://example.test/");

        Assert.Equal(bodyText, await response.Content.ReadAsStringAsync());

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.Contains(bodyText, outgoing.ResponseBody);
    }

    [Fact]
    public async Task Null_content_returns_empty_string()
    {
        var options = new DebugProbeOptions();
        var (client, entry) = BuildClient(options, responseContent: null);

        await client.GetAsync("https://example.test/");

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.Equal(string.Empty, outgoing.ResponseBody);
    }

    [Fact]
    public async Task Non_text_content_returns_empty_string()
    {
        var binaryContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]); // PNG header bytes
        binaryContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var options = new DebugProbeOptions();
        var (client, entry) = BuildClient(options, responseContent: binaryContent);

        await client.GetAsync("https://example.test/");

        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.Equal(string.Empty, outgoing.ResponseBody);
    }

    // ---------------------------------------------------------------------------
    // Stub inner handler
    // ---------------------------------------------------------------------------

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
