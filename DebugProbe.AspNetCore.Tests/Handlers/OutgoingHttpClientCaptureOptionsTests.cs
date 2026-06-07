using System.Net;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DebugProbe.AspNetCore.Tests.Handlers;

public class OutgoingHttpClientCaptureOptionsTests
{
    [Fact]
    public async Task Disabled_outgoing_capture_does_not_store_outgoing_traces()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints =>
            {
                endpoints.MapGet("/proxy", async (IHttpClientFactory httpClientFactory) =>
                {
                    var client = httpClientFactory.CreateClient("outgoing");
                    var body = await client.GetStringAsync("https://api.example.test/ping");

                    return Results.Text(body);
                });
            },
            configureOptions: options => options.CaptureOutgoingHttpClientRequests = false,
            configureServices: services =>
            {
                services.AddHttpClient("outgoing")
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("pong")
                        }));
            });

        var response = await app.Client.GetAsync("/proxy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());

        var entry = app.SingleEntry;
        Assert.Equal("GET", entry.Method);
        Assert.Equal("/proxy", entry.Path);
        Assert.Empty(entry.OutgoingRequests);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }
}
