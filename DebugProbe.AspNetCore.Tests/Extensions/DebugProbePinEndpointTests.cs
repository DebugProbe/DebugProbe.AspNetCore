using System.Net;
using System.Text.Json;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DebugProbe.AspNetCore.Tests.Extensions;

public class DebugProbePinEndpointTests
{
    [Fact]
    public async Task Pin_endpoint_toggles_pin_state_and_returns_200()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")));

        await app.Client.GetAsync("/hello");
        var entry = app.SingleEntry;

        var pinRes = await app.Client.PostAsync($"/debug/pin/{entry.Id}", null);
        Assert.Equal(HttpStatusCode.OK, pinRes.StatusCode);

        var body = JsonDocument.Parse(await pinRes.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(entry.Id, body.GetProperty("id").GetString());
        Assert.True(body.GetProperty("isPinned").GetBoolean());

        // Second call toggles back to unpinned
        var unpinRes = await app.Client.PostAsync($"/debug/pin/{entry.Id}", null);
        Assert.Equal(HttpStatusCode.OK, unpinRes.StatusCode);

        var unpinBody = JsonDocument.Parse(await unpinRes.Content.ReadAsStringAsync()).RootElement;
        Assert.False(unpinBody.GetProperty("isPinned").GetBoolean());
    }

    [Fact]
    public async Task Pin_endpoint_returns_409_when_cap_is_reached()
    {
        // MaxEntries large enough that normal eviction doesn't interfere
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")),
            options => options.MaxEntries = 100);

        // Create MaxPinnedEntries + 1 entries
        for (int i = 0; i <= DebugEntryStore.MaxPinnedEntries; i++)
        {
            await app.Client.GetAsync("/hello");
        }

        var all = app.Store.GetAll();

        // Pin the first MaxPinnedEntries entries
        for (int i = 0; i < DebugEntryStore.MaxPinnedEntries; i++)
        {
            var pinRes = await app.Client.PostAsync($"/debug/pin/{all[i].Id}", null);
            Assert.Equal(HttpStatusCode.OK, pinRes.StatusCode);
        }

        // Attempt to pin one more — should 409
        var overflow = all[DebugEntryStore.MaxPinnedEntries];
        var overflowRes = await app.Client.PostAsync($"/debug/pin/{overflow.Id}", null);
        Assert.Equal(HttpStatusCode.Conflict, overflowRes.StatusCode);

        var errBody = JsonDocument.Parse(await overflowRes.Content.ReadAsStringAsync()).RootElement;
        Assert.True(errBody.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Pin_endpoint_is_not_mapped_in_production_by_default()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")));

        await app.Client.GetAsync("/hello");
        var entry = app.SingleEntry;

        var res = await app.Client.PostAsync($"/debug/pin/{entry.Id}", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Pin_endpoint_is_mapped_in_production_when_allowed()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")),
            options => options.AllowUiInProduction = true);

        await app.Client.GetAsync("/hello");
        var entry = app.SingleEntry;

        var res = await app.Client.PostAsync($"/debug/pin/{entry.Id}", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
