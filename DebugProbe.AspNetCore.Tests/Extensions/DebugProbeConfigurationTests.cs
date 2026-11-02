using System.Net;
using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Tests.Extensions;

public class DebugProbeConfigurationTests
{
    [Fact]
    public async Task Custom_prefix_applies_to_navigation_assets_compare_and_clear()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("hello")),
            options => options.RoutePrefix = "tools/probe/");

        await app.Client.GetAsync("/hello");
        var id = app.SingleEntry.Id;
        var html = await app.Client.GetStringAsync("/tools/probe");
        Assert.Contains($"data-url=\"/tools/probe/{id}\"", html);
        Assert.Contains("src=\"/tools/probe/js/debugprobe-ui.js\"", html);
        Assert.Contains("data-debugprobe-prefix=\"/tools/probe\"", html);
        Assert.DoesNotContain("{{route_prefix}}", html);
        var details = await app.Client.GetStringAsync($"/tools/probe/{id}");
        Assert.Contains("href=\"/tools/probe\"", details);
        var compare = await app.Client.GetStringAsync($"/tools/probe/compare?localTraceId={id}");
        Assert.Contains("data-debugprobe-prefix=\"/tools/probe\"", compare);
        var script = await app.Client.GetStringAsync("/tools/probe/js/debugprobe-ui.js");
        Assert.Contains("dataset.debugprobePrefix", script);
        Assert.DoesNotContain("/debug/clear", script);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/debug")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client.PostAsync("/tools/probe/clear", null)).StatusCode);
        Assert.Empty(app.Store.GetAll());
    }

    [Fact]
    public async Task Disabled_probe_leaves_application_working_without_capture_or_endpoints()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("hello")),
            options => options.Enabled = false);

        Assert.Equal("hello", await app.Client.GetStringAsync("/hello"));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/debug")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/debug/environment")).StatusCode);
        Assert.Empty(app.Store.GetAll());
    }

    [Fact]
    public void Options_can_be_bound_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DebugProbe:MaxRequests"] = "50",
            ["DebugProbe:RoutePrefix"] = "tools/probe/",
            ["DebugProbe:SlowRequestThreshold"] = "00:00:02",
            ["DebugProbe:CaptureResponseBody"] = "false"
        }).Build();
        var services = new ServiceCollection();
        services.AddDebugProbe();
        services.Configure<DebugProbeOptions>(configuration.GetSection("DebugProbe"));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DebugProbeOptions>>().Value;
        Assert.Same(options, provider.GetRequiredService<DebugProbeOptions>());
        Assert.Equal(50, options.MaxRequests);
        Assert.Equal("/tools/probe", options.RoutePrefix);
        Assert.Equal(TimeSpan.FromSeconds(2), options.SlowRequestThreshold);
        Assert.False(options.CaptureResponseBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/bad?query")]
    public void Invalid_prefix_is_rejected(string prefix)
    {
        var services = new ServiceCollection();
        services.AddDebugProbe(options => options.RoutePrefix = prefix);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<DebugProbeOptions>());
    }
}
