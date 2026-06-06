using System.Text;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class RedactionTests
{
    [Fact]
    public async Task Redacts_configured_headers_query_parameters_and_json_fields()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapPost("/orders", async context =>
            {
                context.Response.Headers["X-Session-Token"] = "response-secret";
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"ok\":true,\"refreshToken\":\"response-token\"}");
            }),
            ConfigureRedaction);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders?api_key=query-secret&safe=yes")
        {
            Content = new StringContent(
                "{\"name\":\"Ada\",\"password\":\"secret\",\"profile\":{\"refreshToken\":\"nested-token\"}}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-Api-Key", "header-secret");

        await app.Client.SendAsync(request);

        var entry = app.SingleEntry;

        Assert.Equal("[REDACTED]", entry.RequestHeaders["X-Api-Key"]);
        Assert.Equal("[REDACTED]", entry.ResponseHeaders["X-Session-Token"]);
        Assert.Equal("?api_key=[REDACTED]&safe=yes", entry.Query);
        Assert.Equal("http://localhost/orders?api_key=[REDACTED]&safe=yes", entry.RequestUrl);
        Assert.Contains("\"password\":\"[REDACTED]\"", entry.RequestBody);
        Assert.Contains("\"refreshToken\":\"[REDACTED]\"", entry.RequestBody);
        Assert.Contains("\"refreshToken\":\"[REDACTED]\"", entry.ResponseBody);
        Assert.DoesNotContain("secret", entry.RequestBody);
        Assert.DoesNotContain("response-token", entry.ResponseBody);
    }

    [Fact]
    public async Task Leaves_invalid_json_body_unchanged_when_json_fields_are_configured()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapPost("/text", async context =>
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("ok");
            }),
            options => options.RedactedJsonFields = ["password"]);

        await app.Client.PostAsync(
            "/text",
            new StringContent("password=secret", Encoding.UTF8, "text/plain"));

        Assert.Equal("password=secret", app.SingleEntry.RequestBody);
    }

    private static void ConfigureRedaction(DebugProbeOptions options)
    {
        options.RedactedHeaders =
        [
            ..options.RedactedHeaders,
            "X-Api-Key",
            "X-Session-Token"
        ];

        options.RedactedQueryParameters = ["api_key"];
        options.RedactedJsonFields = ["password", "refreshToken"];
    }
}
