using System.Net.Http.Headers;
using System.Text;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class RequestBodyCaptureTests
{
    [Fact]
    public async Task Captures_json_request_body()
    {
        await using var app = await CreateEchoAppAsync();
        var content = JsonContent("{\"name\":\"Ada\"}");

        await app.Client.PostAsync("/echo", content);

        Assert.Equal("{\"name\":\"Ada\"}", app.SingleEntry.RequestBody);
    }

    [Fact]
    public async Task Handles_empty_request_body()
    {
        await using var app = await CreateEchoAppAsync();

        await app.Client.PostAsync("/echo", new StringContent(string.Empty));

        Assert.Equal(string.Empty, app.SingleEntry.RequestBody);
    }

    [Fact]
    public async Task Handles_large_request_body_within_limit()
    {
        await using var app = await CreateEchoAppAsync(options => options.MaxBodyCaptureSizeKb = 2);
        var body = new string('a', 1500);

        await app.Client.PostAsync("/echo", JsonContent(body));

        Assert.Equal(body, app.SingleEntry.RequestBody);
    }

    [Fact]
    public async Task Respects_max_body_capture_size_for_request()
    {
        await using var app = await CreateEchoAppAsync(options => options.MaxBodyCaptureSizeKb = 1);
        var body = new string('a', 1200);

        await app.Client.PostAsync("/echo", JsonContent(body));

        Assert.Equal("[Body too large]", app.SingleEntry.RequestBody);
    }

    [Fact]
    public async Task Handles_non_text_request_content_safely()
    {
        await using var app = await CreateEchoAppAsync();
        var content = new ByteArrayContent([0, 1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        await app.Client.PostAsync("/echo", content);

        Assert.Equal("[Body not captured: non-text content]", app.SingleEntry.RequestBody);
    }

    private static Task<DebugProbeTestApp> CreateEchoAppAsync(Action<DebugProbeOptions>? configureOptions = null)
    {
        return DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapPost("/echo", async context =>
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(body);
            }),
            configureOptions);
    }

    private static StringContent JsonContent(string value)
    {
        return new StringContent(value, Encoding.UTF8, "application/json");
    }
}
