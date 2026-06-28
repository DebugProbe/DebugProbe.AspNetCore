using System.Net;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace DebugProbe.AspNetCore.Tests.Extensions;

public class DebugProbeProductionEndpointTests
{
    [Fact]
    public async Task Production_does_not_map_ui_endpoints_by_default()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")));

        var capturedResponse = await app.Client.GetAsync("/hello");
        var debugResponse = await app.Client.GetAsync("/debug");
        var comparePageResponse = await app.Client.GetAsync($"/compare?localTraceId={app.SingleEntry.Id}");
        var scriptResponse = await app.Client.GetAsync("/debug/js/debugprobe-ui.js");
        var logoResponse = await app.Client.GetAsync("/debug/logo.png");
        var environmentResponse = await app.Client.GetAsync("/debug/environment");
        var jsonResponse = await app.Client.GetAsync($"/debug/json/{app.SingleEntry.Id}");
        var clearResponse = await app.Client.PostAsync("/debug/clear", null);

        Assert.Equal(HttpStatusCode.OK, capturedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, debugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, comparePageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scriptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logoResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, clearResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, environmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jsonResponse.StatusCode);
    }

    [Fact]
    public async Task Production_maps_ui_endpoints_when_explicitly_allowed()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")),
            options => options.AllowUiInProduction = true);

        await app.Client.GetAsync("/hello");

        var debugResponse = await app.Client.GetAsync("/debug");
        var detailsResponse = await app.Client.GetAsync($"/debug/{app.SingleEntry.Id}");
        var comparePageResponse = await app.Client.GetAsync($"/compare?localTraceId={app.SingleEntry.Id}");
        var scriptResponse = await app.Client.GetAsync("/debug/js/debugprobe-ui.js");
        var logoResponse = await app.Client.GetAsync("/debug/logo.png");
        var environmentResponse = await app.Client.GetAsync("/debug/environment");
        var jsonResponse = await app.Client.GetAsync($"/debug/json/{app.SingleEntry.Id}");
        var clearResponse = await app.Client.PostAsync("/debug/clear", null);

        Assert.Equal(HttpStatusCode.OK, debugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, comparePageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logoResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, environmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
    }

    [Fact]
    public async Task Production_blocks_machine_readable_endpoints_by_default()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")));

        await app.Client.GetAsync("/hello");

        var environmentResponse = await app.Client.GetAsync("/debug/environment");
        var jsonResponse = await app.Client.GetAsync($"/debug/json/{app.SingleEntry.Id}");
        var compareResponse = await app.Client.GetAsync(
            $"/debug/compare/{app.SingleEntry.Id}?baseUrl=http://localhost&remoteTraceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, environmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, jsonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, compareResponse.StatusCode);
    }
}
