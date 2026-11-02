using System.Net;
using System.Text.Json;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DebugProbe.AspNetCore.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task Parallel_adds_preserve_all_counts_and_keep_latest_requests()
    {
        const int total = 2000;
        const int limit = 20;
        var store = new DebugEntryStore(new DebugProbeOptions { MaxRequests = limit });

        await Parallel.ForAsync(0, total, (i, _) =>
        {
            store.Add(new DebugEntry { Id = i.ToString(), ResponseBody = "System.Exception: same failure" });
            Assert.InRange(store.GetAll().Count, 1, limit);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(limit, store.GetAll().Count);
        Assert.Equal(limit, store.GetAll().Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(total, Assert.Single(store.ExceptionGroups.Values).Count);
    }

    [Fact]
    public async Task Parallel_add_clear_and_reads_remain_bounded_and_consistent()
    {
        const int limit = 10;
        var store = new DebugEntryStore(new DebugProbeOptions { MaxRequests = limit });
        var remoteEnvironment = new DebugEnvironment { Environment = "remote" };

        await Parallel.ForAsync(0, 2000, (i, _) =>
        {
            if (i % 7 == 0)
                store.Clear();
            else
                store.Add(new DebugEntry { Id = i.ToString(), ResponseBody = $"Type{i}Exception: failure" }, remoteEnvironment);

            var requests = store.GetAll();
            var groups = store.ExceptionGroups;
            Assert.InRange(requests.Count, 0, limit);
            Assert.InRange(groups.Count, 0, limit);
            foreach (var entry in requests)
            {
                store.Get(entry.Id);
                store.GetEnvironment(entry);
            }
            Assert.All(groups, pair => Assert.Equal(pair.Key, pair.Value.Fingerprint));
            return ValueTask.CompletedTask;
        });

        var previous = store.GetAll();
        store.Clear();
        Assert.Empty(store.GetAll());
        Assert.Empty(store.ExceptionGroups);
        Assert.All(previous, entry => Assert.Same(store.Environment, store.GetEnvironment(entry)));
        store.Add(new DebugEntry { Id = "fresh", ResponseBody = "System.Exception: fresh" });
        Assert.Equal("fresh", Assert.Single(store.GetAll()).Id);
        Assert.Equal(1, Assert.Single(store.ExceptionGroups.Values).Count);
    }

    [Fact]
    public void Exception_group_snapshots_remain_stable_after_updates_and_clear()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry = new DebugEntry { Id = "test", ResponseBody = "System.Exception: failure" };
        store.Add(entry);
        var snapshot = store.ExceptionGroups;
        store.Add(entry);
        Assert.Equal(1, Assert.Single(snapshot.Values).Count);
        Assert.Equal(2, Assert.Single(store.ExceptionGroups.Values).Count);
        store.Clear();
        Assert.Single(snapshot);
    }

    [Fact]
    public async Task Parallel_http_calls_are_recorded_once_and_attached_to_the_correct_request()
    {
        const int requestCount = 12;
        const int outgoingCount = 64;
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/proxy/{id}", async (string id, IHttpClientFactory factory) =>
            {
                using var client = factory.CreateClient("remote");
                await Task.WhenAll(Enumerable.Range(0, outgoingCount).Select(async i =>
                {
                    using var response = await client.GetAsync($"https://remote.test/{id}/{i}");
                    response.EnsureSuccessStatusCode();
                }));
                return Results.Text("ok");
            }),
            options =>
            {
                options.MaxRequests = requestCount;
                options.CaptureRequestBody = false;
                options.CaptureResponseBody = false;
            },
            configureServices: services => services.AddHttpClient("remote")
                .ConfigurePrimaryHttpMessageHandler(() => new ParallelHandler()));

        await Task.WhenAll(Enumerable.Range(0, requestCount).Select(async id =>
            Assert.Equal("ok", await app.Client.GetStringAsync($"/proxy/{id}"))));

        var requests = app.Store.GetAll();
        Assert.Equal(requestCount, requests.Count);
        foreach (var entry in requests)
        {
            var id = entry.Path.Split('/').Last();
            Assert.Equal(outgoingCount, entry.OutgoingRequests.Count);
            Assert.Equal(outgoingCount, entry.OutgoingRequests.Select(call => call.Url).Distinct().Count());
            Assert.All(entry.OutgoingRequests, call => Assert.StartsWith($"https://remote.test/{id}/", call.Url));

            // Remote comparison uses System.Text.Json to round-trip captured traces.
            var copy = JsonSerializer.Deserialize<DebugEntry>(JsonSerializer.Serialize(entry))!;
            Assert.Equal(entry.OutgoingRequests.Select(call => call.Url), copy.OutgoingRequests.Select(call => call.Url));
        }
    }

    private sealed class ParallelHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }
}
