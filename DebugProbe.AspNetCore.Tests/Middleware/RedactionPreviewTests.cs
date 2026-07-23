using System.Text;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Xunit;

namespace DebugProbe.AspNetCore.Tests.Middleware;

/// <summary>
/// Tests that AllowRedactionPreview populates OriginalXxx fields on DebugEntry
/// only when the feature is enabled, and never modifies the redacted values.
/// </summary>
public class RedactionPreviewTests
{
    [Fact]
    public async Task AllowRedactionPreview_false_does_not_populate_original_fields()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapPost("/orders", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"ok\":true,\"refreshToken\":\"response-token\"}");
            }),
            options =>
            {
                options.RedactedHeaders = [.. options.RedactedHeaders, "X-Api-Key"];
                options.RedactedJsonFields = ["password"];
                options.AllowRedactionPreview = false; // explicit default
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = new StringContent("{\"password\":\"secret\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Api-Key", "header-secret");

        await app.Client.SendAsync(request);
        var entry = app.SingleEntry;

        // Redaction must still be applied to the regular fields
        Assert.Equal("[REDACTED]", entry.RequestHeaders["X-Api-Key"]);
        Assert.Contains("\"password\":\"[REDACTED]\"", entry.RequestBody);

        // Original fields must be empty when preview is disabled
        Assert.Empty(entry.OriginalRequestHeaders);
        Assert.Null(entry.OriginalRequestBody);
        Assert.Null(entry.OriginalResponseBody);
        Assert.Null(entry.OriginalQuery);
    }

    [Fact]
    public async Task AllowRedactionPreview_true_populates_original_fields_alongside_redacted()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapPost("/orders", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"ok\":true,\"refreshToken\":\"response-token\"}");
            }),
            options =>
            {
                options.RedactedHeaders = [.. options.RedactedHeaders, "X-Api-Key"];
                options.RedactedJsonFields = ["password", "refreshToken"];
                options.AllowRedactionPreview = true;
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders?api_key=secret")
        {
            Content = new StringContent("{\"password\":\"s3cr3t\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Api-Key", "header-secret");

        await app.Client.SendAsync(request);
        var entry = app.SingleEntry;

        // Redacted values must still be applied as normal
        Assert.Equal("[REDACTED]", entry.RequestHeaders["X-Api-Key"]);
        Assert.Contains("\"password\":\"[REDACTED]\"", entry.RequestBody);

        // Original headers must contain the raw value
        Assert.True(entry.OriginalRequestHeaders.ContainsKey("X-Api-Key"));
        Assert.Equal("header-secret", entry.OriginalRequestHeaders["X-Api-Key"]);

        // Original body must contain the real secret
        Assert.NotNull(entry.OriginalRequestBody);
        Assert.Contains("s3cr3t", entry.OriginalRequestBody);

        // Original response body must contain the raw token
        Assert.NotNull(entry.OriginalResponseBody);
        Assert.Contains("response-token", entry.OriginalResponseBody);
    }
}
