using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Tests.Configuration;

public class DebugProbeOptionsTests
{
    [Fact]
    public void Defaults_work_correctly()
    {
        var options = new DebugProbeOptions();

        Assert.Equal(20, options.MaxRequests);
        Assert.Equal(32, options.MaxBodySizeKb);
        Assert.Null(options.AllowLocalCompareTargets);
        Assert.False(options.AllowUiInProduction);
        Assert.Null(options.AuthorizationPolicy);
        Assert.True(options.CaptureOutgoingRequests);
        Assert.Contains("/health", options.ExcludedPaths);
        Assert.True(options.Enabled);
        Assert.True(options.CaptureRequestBody);
        Assert.True(options.CaptureResponseBody);
        Assert.Equal(["Authorization", "Cookie", "Set-Cookie"], options.RedactedHeaders);
        Assert.Empty(options.RedactedQueryParameters);
        Assert.Empty(options.RedactedJsonFields);
    }

    [Fact]
    public void Custom_options_are_registered_and_used()
    {
        var services = new ServiceCollection();

        services.AddDebugProbe(options =>
        {
            options.MaxRequests = 2;
            options.MaxBodySizeKb = 4;
            options.AllowLocalCompareTargets = true;
            options.AuthorizationPolicy = "DebugProbePolicy";
            options.ExcludedPaths = ["/health"];
            options.CaptureOutgoingRequests = false;
            options.RedactedHeaders = ["X-Api-Key"];
            options.RedactedQueryParameters = ["token"];
            options.RedactedJsonFields = ["password"];
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DebugProbeOptions>();
        var store = provider.GetRequiredService<DebugEntryStore>();

        Assert.Equal(2, options.MaxRequests);
        Assert.Equal(4, options.MaxBodySizeKb);
        Assert.True(options.AllowLocalCompareTargets);
        Assert.Equal("DebugProbePolicy", options.AuthorizationPolicy);
        Assert.Equal(["/health"], options.ExcludedPaths);
        Assert.False(options.CaptureOutgoingRequests);
        Assert.Equal(["X-Api-Key"], options.RedactedHeaders);
        Assert.Equal(["token"], options.RedactedQueryParameters);
        Assert.Equal(["password"], options.RedactedJsonFields);
        Assert.NotNull(store.Environment);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_request_limit_is_rejected(int limit)
    {
        var services = new ServiceCollection();
        services.AddDebugProbe(options => options.MaxRequests = limit);
        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<DebugProbeOptions>());
        Assert.Contains("MaxRequests", exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Invalid_body_limit_is_rejected(int limit)
    {
        var services = new ServiceCollection();
        services.AddDebugProbe(options => options.MaxBodySizeKb = limit);
        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<DebugProbeOptions>());
        Assert.Contains("MaxBodySizeKb", exception.Message);
    }

    [Fact]
    public void MaxBodySizeKb_zero_is_valid()
    {
        var options = new DebugProbeOptions();
        options.MaxBodySizeKb = 0;

        Assert.Equal(0, options.MaxBodySizeKb);
    }

    [Fact]
    public void SlowRequestThreshold_defaults_to_1000()
    {
        var options = new DebugProbeOptions();
        Assert.Equal(TimeSpan.FromSeconds(1), options.SlowRequestThreshold);
    }

    [Fact]
    public void SlowRequestThreshold_can_be_configured()
    {
        var services = new ServiceCollection();
        services.AddDebugProbe(options =>
        {
            options.SlowRequestThreshold = TimeSpan.FromMilliseconds(500);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DebugProbeOptions>();
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.SlowRequestThreshold);
    }
}
