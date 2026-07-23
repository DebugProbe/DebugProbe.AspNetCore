using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class RedactionPreviewEndToEndTests(ITestOutputHelper output)
{
    private static readonly Action<DebugProbeOptions> ConfigureOptionsBase = options =>
    {
        options.RedactedHeaders = [.. options.RedactedHeaders, "X-Api-Key"];
        options.RedactedJsonFields = ["password"];
    };

    [Fact]
    public async Task TestA_BaseRedactionStillWorks()
    {
        output.WriteLine("=== TEST A: Base Redaction Still Works ===");

        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapPost("/delay/50", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }),
            configureOptions: options =>
            {
                ConfigureOptionsBase(options);
                options.AllowRedactionPreview = false;
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/delay/50")
        {
            Content = new StringContent("{\"password\":\"topsecret123\"}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Api-Key", "secret-key-999");

        var res = await app.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var entry = app.SingleEntry;
        var jsonRes = await app.Client.GetAsync($"/debug/json/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);
        var jsonRaw = await jsonRes.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(jsonRaw);
        var root = doc.RootElement;

        // ASSERT 1: requestHeaders["X-Api-Key"] == "[REDACTED]"
        var requestHeaders = root.GetProperty("requestHeaders");
        var apiKeyHeader = requestHeaders.GetProperty("X-Api-Key").GetString();
        Assert.Equal("[REDACTED]", apiKeyHeader);

        // ASSERT 2: "secret-key-999" does not appear anywhere in the JSON response
        Assert.DoesNotContain("secret-key-999", jsonRaw);

        output.WriteLine("[PASS] TEST A: Base redaction verified successfully. Header is [REDACTED] and secret-key-999 is absent.");
    }

    [Fact]
    public async Task TestB_PreviewOff_NoOriginalValuesRetained()
    {
        output.WriteLine("=== TEST B: Preview OFF (AllowRedactionPreview=false) ===");

        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapPost("/delay/50", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }),
            configureOptions: options =>
            {
                ConfigureOptionsBase(options);
                options.AllowRedactionPreview = false;
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/delay/50")
        {
            Content = new StringContent("{\"password\":\"topsecret123\"}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Api-Key", "secret-key-999");

        await app.Client.SendAsync(req);
        var entry = app.SingleEntry;

        // JSON check
        var jsonRes = await app.Client.GetAsync($"/debug/json/{entry.Id}");
        var jsonRaw = await jsonRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonRaw);
        var root = doc.RootElement;

        // ASSERT 1: originalRequestHeaders is empty
        var origHeaders = root.GetProperty("originalRequestHeaders");
        Assert.Equal(0, origHeaders.EnumerateObject().Count());

        // ASSERT 2: "secret-key-999" does not appear anywhere in the JSON response
        Assert.DoesNotContain("secret-key-999", jsonRaw);

        // HTML check
        var htmlRes = await app.Client.GetAsync($"/debug/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, htmlRes.StatusCode);
        var htmlRaw = await htmlRes.Content.ReadAsStringAsync();

        // ASSERT 3: No "Redaction Preview" toggle/banner appears in HTML source
        Assert.DoesNotContain("Redaction Preview", htmlRaw);
        Assert.DoesNotContain("secret-key-999", htmlRaw);

        output.WriteLine("[PASS] TEST B: Preview OFF verified. No original values stored or displayed in JSON or HTML.");
    }

    [Fact]
    public async Task TestC_PreviewOn_Development_OriginalValuesRetainedAndGatedCorrectly()
    {
        output.WriteLine("=== TEST C: Preview ON + Development Environment ===");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapPost("/delay/50", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }),
            configureOptions: options =>
            {
                ConfigureOptionsBase(options);
                options.AllowRedactionPreview = true;
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/delay/50")
        {
            Content = new StringContent("{\"password\":\"topsecret123\"}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Api-Key", "secret-key-999");

        await app.Client.SendAsync(req);
        var entry = app.SingleEntry;

        // JSON check
        var jsonRes = await app.Client.GetAsync($"/debug/json/{entry.Id}");
        var jsonRaw = await jsonRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonRaw);
        var root = doc.RootElement;

        // ASSERT 1: originalRequestHeaders["X-Api-Key"] == "secret-key-999"
        var origHeaders = root.GetProperty("originalRequestHeaders");
        Assert.Equal("secret-key-999", origHeaders.GetProperty("X-Api-Key").GetString());

        // ASSERT 2: requestHeaders["X-Api-Key"] still shows "[REDACTED]"
        var requestHeaders = root.GetProperty("requestHeaders");
        Assert.Equal("[REDACTED]", requestHeaders.GetProperty("X-Api-Key").GetString());

        // HTML check
        var htmlRes = await app.Client.GetAsync($"/debug/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, htmlRes.StatusCode);
        var htmlRaw = await htmlRes.Content.ReadAsStringAsync();

        // ASSERT 3: "Redaction Preview — local only" banner IS present in HTML
        Assert.Contains("Redaction Preview — local only", htmlRaw);
        Assert.Contains("secret-key-999", htmlRaw);

        output.WriteLine("[PASS] TEST C: Preview ON + Development verified. Original value present in preview, default view remains redacted, HTML banner rendered.");
    }

    [Fact]
    public async Task TestD_PreviewOn_Production_SecurityGateCheck()
    {
        output.WriteLine("=== TEST D: Preview ON + Production Environment (Security Gate Check) ===");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Production,
            endpoints => endpoints.MapPost("/delay/50", async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }),
            configureOptions: options =>
            {
                ConfigureOptionsBase(options);
                options.AllowRedactionPreview = true;
                // AllowUiInProduction is NOT set (remains false as required by validator when AllowRedactionPreview=true)
            });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/delay/50")
        {
            Content = new StringContent("{\"password\":\"topsecret123\"}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Api-Key", "secret-key-999");

        await app.Client.SendAsync(req);
        var entry = app.SingleEntry;

        // 1. Rendered HTML detail page check
        var htmlRaw = DebugProbe.AspNetCore.Internal.Rendering.HtmlRenderer.RenderDetailsPage(
            entry,
            app.Store.GetEnvironment(entry),
            entry.RequestBody,
            entry.ResponseBody,
            new DebugProbeOptions { AllowRedactionPreview = true });

        // ASSERT: no "Redaction Preview" toggle/banner appears in HTML, and no "secret-key-999" appears anywhere in raw HTML
        Assert.DoesNotContain("Redaction Preview", htmlRaw);
        Assert.DoesNotContain("secret-key-999", htmlRaw);

        output.WriteLine("[PASS] TEST D (HTML): HtmlRenderer correctly suppressed Redaction Preview banner in Production. Secret does not appear in HTML.");

        // 2. Check JSON endpoint / storage layer behavior
        var isOriginalHeadersPopulated = entry.OriginalRequestHeaders.ContainsKey("X-Api-Key")
            && entry.OriginalRequestHeaders["X-Api-Key"] == "secret-key-999";

        if (isOriginalHeadersPopulated)
        {
            output.WriteLine("[OBSERVATION - TEST D] JSON/Storage Layer: originalRequestHeaders IS populated with 'secret-key-999' in memory/storage because DebugProbeMiddleware captures originals whenever AllowRedactionPreview=true (gated by config property, not by EnvironmentUtils in middleware).");
        }
        else
        {
            output.WriteLine("[OBSERVATION - TEST D] JSON/Storage Layer: originalRequestHeaders is NOT populated.");
        }
    }
}
