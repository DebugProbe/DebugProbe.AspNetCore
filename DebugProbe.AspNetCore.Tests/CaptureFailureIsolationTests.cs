using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DebugProbe.AspNetCore.Handlers;
using DebugProbe.AspNetCore.Internal.Streams;
using DebugProbe.AspNetCore.Middleware;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;

namespace DebugProbe.AspNetCore.Tests;

public class CaptureFailureIsolationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Incoming_capture_read_failure_preserves_body_and_runs_application_once(bool seekable)
    {
        var context = CreateContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "text/plain";
        context.Request.Body = new FailOnceStream(seekable);
        var calls = 0;
        var middleware = new DebugProbeMiddleware(async ctx =>
        {
            calls++;
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            Assert.Equal("hello", await reader.ReadToEndAsync());
            await ctx.Response.WriteAsync("ok");
        }, new DebugProbeOptions());

        await middleware.Invoke(context, new DebugEntryStore(new DebugProbeOptions()));

        Assert.Equal(1, calls);
        Assert.Equal("ok", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalization_failure_preserves_response_or_original_application_exception(bool failApplication)
    {
        var context = CreateContext();
        var originalBody = context.Response.Body;
        var options = new DebugProbeOptions();
        var store = new DebugEntryStore(options);
        var originalException = new InvalidOperationException("application failed");
        var calls = 0;
        var middleware = new DebugProbeMiddleware(async ctx =>
        {
            calls++;
            await ctx.Response.WriteAsync("original response");
            // Simulate a diagnostics-only failure while formatting captured headers.
            ctx.Request.Headers["X-Test"] = "value";
            options.RedactedHeaders = null!;
            if (failApplication)
                throw originalException;
        }, options);

        if (failApplication)
            Assert.Same(originalException, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context, store)));
        else
            await middleware.Invoke(context, store);

        Assert.Equal(1, calls);
        Assert.Same(originalBody, context.Response.Body);
        Assert.Equal("original response", Encoding.UTF8.GetString(((MemoryStream)originalBody).ToArray()));
        Assert.False(context.Items.ContainsKey("DebugProbeEntry"));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task Exception_formatting_failure_does_not_replace_application_exception()
    {
        var original = new UnformattableException();
        var context = CreateContext();
        var originalBody = context.Response.Body;
        var middleware = new DebugProbeMiddleware(_ => throw original, new DebugProbeOptions());

        Assert.Same(original, await Assert.ThrowsAsync<UnformattableException>(() =>
            middleware.Invoke(context, new DebugEntryStore(new DebugProbeOptions()))));
        Assert.Same(originalBody, context.Response.Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Outgoing_partial_capture_failure_replays_all_response_bytes(bool seekable)
    {
        var context = CreateContext();
        var entry = new DebugEntry();
        context.Items["DebugProbeEntry"] = entry;
        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StreamContent(new FailOnceStream(seekable))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        response.Headers.Add("X-Original", "yes");
        var transport = new StubHandler(_ => Task.FromResult(response));
        using var client = CreateClient(context, transport);

        using var result = await client.GetAsync("https://example.test", HttpCompletionOption.ResponseHeadersRead);

        Assert.Same(response, result);
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        Assert.Equal("yes", Assert.Single(result.Headers.GetValues("X-Original")));
        Assert.Equal("hello", await result.Content.ReadAsStringAsync());
        Assert.Equal(1, transport.Calls);
        Assert.Empty(entry.OutgoingRequests);
    }

    [Fact]
    public async Task Outgoing_capture_failure_does_not_replace_transport_exception()
    {
        var original = new UnformattableException();
        var context = CreateContext();
        context.Items["DebugProbeEntry"] = new DebugEntry();
        var transport = new StubHandler(_ => throw original);
        using var client = CreateClient(context, transport);

        Assert.Same(original, await Assert.ThrowsAsync<UnformattableException>(() => client.GetAsync("https://example.test")));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Transport_cancellation_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = CreateContext();
        context.Items["DebugProbeEntry"] = new DebugEntry();
        var transport = new StubHandler(_ => Task.FromCanceled<HttpResponseMessage>(cancellation.Token));
        using var client = CreateClient(context, transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test"));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Diagnostic_copy_failure_does_not_block_response_writes()
    {
        using var original = new MemoryStream();
        var capture = new BoundedResponseCaptureStream(original, 32);
        capture.Dispose(); // Make the diagnostic memory buffer unavailable.

        await capture.WriteAsync("hello"u8.ToArray());

        Assert.Equal("hello", Encoding.UTF8.GetString(original.ToArray()));
    }

    [Fact]
    public async Task Application_response_stream_errors_are_not_swallowed()
    {
        using var original = new MemoryStream();
        using var capture = new BoundedResponseCaptureStream(original, 32);
        original.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await capture.WriteAsync("hello"u8.ToArray()));
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.test");
        context.Response.ContentType = "text/plain";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static HttpClient CreateClient(HttpContext context, HttpMessageHandler transport) => new(
        new DebugProbeHttpClientHandler(new HttpContextAccessor { HttpContext = context }, new DebugProbeOptions())
        {
            InnerHandler = transport
        });

    private sealed class UnformattableException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("diagnostic formatting failed");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request);
        }
    }

    // Consume a prefix, fail once without consuming data, then let the application read normally.
    private sealed class FailOnceStream(bool seekable) : MemoryStream("hello"u8.ToArray())
    {
        private int _reads;
        public override bool CanSeek => seekable;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _reads++;
            if (_reads == 2)
                throw new IOException("capture read failed");
            return base.ReadAsync(_reads == 1 ? buffer[..Math.Min(2, buffer.Length)] : buffer, cancellationToken);
        }
    }
}
