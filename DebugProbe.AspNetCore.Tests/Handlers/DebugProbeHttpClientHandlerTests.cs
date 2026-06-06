using System.Net;
using System.Text;
using DebugProbe.AspNetCore.Handlers;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Tests.Handlers;

public class DebugProbeHttpClientHandlerTests
{
    [Fact]
    public async Task Captures_outgoing_http_call_on_active_trace()
    {
        var entry = new DebugEntry();
        var context = new DefaultHttpContext();
        context.Items["DebugProbeEntry"] = entry;

        using var handler = new DebugProbeHttpClientHandler(
            new HttpContextAccessor { HttpContext = context },
            new DebugProbeOptions())
        {
            InnerHandler = new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
                };
                response.Headers.Add("X-Trace", "remote");
                return response;
            })
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.test/orders")
        {
            Content = new StringContent("{\"name\":\"Ada\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var outgoing = Assert.Single(entry.OutgoingRequests);
        Assert.Equal("POST", outgoing.Method);
        Assert.Equal("https://api.example.test/orders", outgoing.Url);
        Assert.Equal(201, outgoing.StatusCode);
        Assert.True(outgoing.IsSuccessStatusCode);
        Assert.Equal("[REDACTED]", outgoing.RequestHeaders["Authorization"]);
        Assert.Equal("remote", outgoing.ResponseHeaders["X-Trace"]);
        Assert.Contains("\"name\": \"Ada\"", outgoing.RequestBody);
        Assert.Contains("\"ok\": true", outgoing.ResponseBody);
    }

    [Fact]
    public async Task Redacts_configured_outgoing_url_headers_and_json_fields()
    {
        var entry = new DebugEntry();
        var context = new DefaultHttpContext();
        context.Items["DebugProbeEntry"] = entry;

        using var handler = new DebugProbeHttpClientHandler(
            new HttpContextAccessor { HttpContext = context },
            new DebugProbeOptions
            {
                RedactedHeaders = ["Authorization", "X-Api-Key"],
                RedactedQueryParameters = ["token"],
                RedactedJsonFields = ["password", "refreshToken"]
            })
        {
            InnerHandler = new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"refreshToken\":\"response-token\"}", Encoding.UTF8, "application/json")
                };
                return response;
            })
        };

        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.test/orders?token=query-secret&safe=yes")
        {
            Content = new StringContent("{\"password\":\"body-secret\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Api-Key", "header-secret");

        await client.SendAsync(request);

        var outgoing = Assert.Single(entry.OutgoingRequests);

        Assert.Equal("https://api.example.test/orders?token=[REDACTED]&safe=yes", outgoing.Url);
        Assert.Equal("[REDACTED]", outgoing.RequestHeaders["X-Api-Key"]);
        Assert.Contains("\"password\": \"[REDACTED]\"", outgoing.RequestBody);
        Assert.Contains("\"refreshToken\": \"[REDACTED]\"", outgoing.ResponseBody);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }
}
