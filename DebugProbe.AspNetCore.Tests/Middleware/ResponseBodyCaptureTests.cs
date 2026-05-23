using DebugProbe.AspNetCore.Tests.Infrastructure;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class ResponseBodyCaptureTests
{
    [Fact]
    public async Task Captures_json_response_body()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/json", () => Results.Json(new { name = "Ada" }));
        });

        await app.Client.GetAsync("/json");

        Assert.Contains("\"name\":\"Ada\"", app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Handles_empty_response()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/empty", () => Results.NoContent());
        });

        await app.Client.GetAsync("/empty");

        Assert.Equal(string.Empty, app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Handles_large_response_within_limit()
    {
        var body = new string('x', 1500);
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/large", () => Results.Text(body, "text/plain")),
            options => options.MaxBodyCaptureSizeKb = 2);

        await app.Client.GetAsync("/large");

        Assert.Equal(body, app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Handles_binary_response_safely()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/binary", () => Results.Bytes([0, 1, 2, 3], "application/octet-stream"));
        });

        await app.Client.GetAsync("/binary");

        Assert.Equal("[Body not captured: non-text content]", app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Validates_bounded_response_capture_behavior()
    {
        var body = new string('x', 1200);
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/too-large", () => Results.Text(body, "text/plain")),
            options => options.MaxBodyCaptureSizeKb = 1);

        var response = await app.Client.GetAsync("/too-large");

        var entry = app.SingleEntry;
        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal("[Body too large]", entry.ResponseBody);
    }
}
