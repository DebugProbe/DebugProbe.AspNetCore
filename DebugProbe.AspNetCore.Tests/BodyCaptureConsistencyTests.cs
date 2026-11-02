using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DebugProbe.AspNetCore.Handlers;
using DebugProbe.AspNetCore.Middleware;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;

namespace DebugProbe.AspNetCore.Tests;

public class BodyCaptureConsistencyTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("under")]
    [InlineData("exact")]
    [InlineData("over")]
    [InlineData("utf8-boundary")]
    [InlineData("utf16-boundary")]
    [InlineData("charset")]
    [InlineData("invalid-charset")]
    [InlineData("unsupported-charset")]
    [InlineData("binary")]
    [InlineData("redacted")]
    [InlineData("truncated-redacted")]
    public async Task All_four_capture_paths_use_the_same_rules_and_preserve_the_payload(string scenario)
    {
        var text = scenario switch
        {
            "empty" => "",
            "exact" => new string('a', 1024),
            "over" => new string('a', 1500),
            "utf8-boundary" => new string('a', 1023) + "€tail",
            "utf16-boundary" => new string('a', 511) + "😀tail",
            "charset" => "café",
            "redacted" => "{\"password\":\"secret\",\"name\":\"Ada\"}",
            "truncated-redacted" => "{\"password\":\"secret\",\"padding\":\"" + new string('a', 1500) + "\"}",
            _ => "hello"
        };
        var encoding = scenario == "charset" ? Encoding.Latin1 : scenario == "utf16-boundary" ? Encoding.Unicode : Encoding.UTF8;
        var contentType = scenario switch
        {
            "charset" => "text/plain; charset=iso-8859-1",
            "utf16-boundary" => "text/plain; charset=utf-16",
            "invalid-charset" => "text/plain; charset=not-a-real-charset",
            "unsupported-charset" => "text/plain; charset=utf-7",
            "binary" => "application/octet-stream",
            "redacted" or "truncated-redacted" => "application/json; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
        var expected = scenario switch
        {
            "over" => new string('a', 1024) + "[truncated]",
            "utf8-boundary" => new string('a', 1023) + "[truncated]",
            "utf16-boundary" => new string('a', 511) + "[truncated]",
            "binary" => "[Body not captured: non-text content]",
            "redacted" => "{\"password\":\"[REDACTED]\",\"name\":\"Ada\"}",
            "truncated-redacted" => "[Body not captured: truncated content cannot be redacted]",
            _ => text
        };
        var bytes = encoding.GetBytes(text);
        var options = new DebugProbeOptions
        {
            MaxBodySizeKb = 1,
            RedactedJsonFields = scenario.Contains("redacted") ? ["password"] : []
        };
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/echo";
        context.Request.ContentType = contentType;
        // No Content-Length: the limit must also work for chunked requests.
        context.Request.Body = new MemoryStream(bytes);
        context.Response.Body = new MemoryStream();
        context.Response.ContentType = contentType;
        var store = new DebugEntryStore(options);
        var middleware = new DebugProbeMiddleware(async ctx =>
        {
            using var received = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(received);
            Assert.Equal(bytes, received.ToArray());
            await ctx.Response.Body.WriteAsync(bytes);
        }, options);

        await middleware.Invoke(context, store);
        var incoming = Assert.Single(store.GetAll());
        Assert.Equal(expected, incoming.RequestBody);
        Assert.Equal(expected, incoming.ResponseBody);
        Assert.Equal(bytes, ((MemoryStream)context.Response.Body).ToArray());

        var entry = new DebugEntry();
        context.Items["DebugProbeEntry"] = entry;
        using var client = new HttpClient(new DebugProbeHttpClientHandler(new HttpContextAccessor { HttpContext = context }, options)
        {
            InnerHandler = new EchoHandler()
        });
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await client.PostAsync("https://example.test/echo", content);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.Equal(expected, outgoing.RequestBody);
        Assert.Equal(expected, outgoing.ResponseBody);
    }

    private sealed class EchoHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(await request.Content!.ReadAsByteArrayAsync(cancellationToken))
            };
            response.Content.Headers.ContentType = request.Content.Headers.ContentType;
            return response;
        }
    }
}
