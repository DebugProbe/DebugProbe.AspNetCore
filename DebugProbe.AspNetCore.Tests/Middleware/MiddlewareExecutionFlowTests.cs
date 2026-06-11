using System.Net;
using DebugProbe.AspNetCore.Middleware;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class MiddlewareExecutionFlowTests
{
    [Fact]
    public async Task Middleware_executes_and_request_continues_through_pipeline()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/hello", () => Results.Text("hello from endpoint"));
        });

        var response = await app.Client.GetAsync("/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello from endpoint", await response.Content.ReadAsStringAsync());

        var entry = app.SingleEntry;
        Assert.Equal("GET", entry.Method);
        Assert.Equal("/hello", entry.Path);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal("hello from endpoint", entry.ResponseBody);
    }

    [Fact]
    public async Task Ignored_paths_are_skipped()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/health", () => Results.Ok()),
            options => options.IgnorePaths = ["/health"]);

        var response = await app.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(app.Store.GetAll());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    [InlineData("/live")]
    public async Task Default_health_probe_paths_are_skipped(string path)
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet(path, () => Results.Ok());
        });

        var response = await app.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(app.Store.GetAll());
    }

    [Fact]
    public async Task Debug_paths_are_skipped()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/debug/custom", () => Results.Text("debug"));
        });

        var response = await app.Client.GetAsync("/debug/custom");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(app.Store.GetAll());
    }

    [Fact]
    public async Task Response_stream_is_restored_after_successful_request()
    {
        var originalBody = new MemoryStream();
        var context = CreateHttpContext(originalBody);
        var store = new DebugEntryStore(new DebugProbeOptions());
        var middleware = new DebugProbeMiddleware(
            async httpContext => await httpContext.Response.WriteAsync("ok"),
            new DebugProbeOptions());

        await middleware.Invoke(context, store);

        Assert.Same(originalBody, context.Response.Body);
        Assert.Equal("ok", Assert.Single(store.GetAll()).ResponseBody);
    }

    [Fact]
    public async Task Response_stream_is_restored_after_exception()
    {
        var originalBody = new MemoryStream();
        var context = CreateHttpContext(originalBody);
        var store = new DebugEntryStore(new DebugProbeOptions());
        var middleware = new DebugProbeMiddleware(
            _ => throw new InvalidOperationException("broken"),
            new DebugProbeOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.Invoke(context, store));

        Assert.Same(originalBody, context.Response.Body);
        var entry = Assert.Single(store.GetAll());
        Assert.Equal(500, entry.StatusCode);
        Assert.Contains("broken", entry.ResponseBody);
    }

    private static DefaultHttpContext CreateHttpContext(Stream responseBody)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.test");
        context.Request.Path = "/direct";
        context.Response.Body = responseBody;
        context.Response.ContentType = "text/plain";
        return context;
    }
}
