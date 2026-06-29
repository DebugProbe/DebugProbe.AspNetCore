using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DebugProbe.AspNetCore.Tests.Configuration;

public class DebugProbeOptionsTests
{
    [Fact]
    public void Defaults_work_correctly()
    {
        var options = new DebugProbeOptions();

        Assert.Equal(20, options.MaxEntries);
        Assert.Equal(32, options.MaxBodyCaptureSizeKb);
        Assert.Null(options.AllowLocalCompareTargets);
        Assert.False(options.AllowUiInProduction);
        Assert.Null(options.AuthorizationPolicy);
        Assert.True(options.CaptureOutgoingHttpClientRequests);
        Assert.Null(options.ServerUrl);
        Assert.Null(options.ApplicationId);
        Assert.Null(options.ApplicationName);
        Assert.Null(options.InstanceId);
        Assert.Empty(options.IgnorePaths);
        Assert.Equal(["Authorization", "Cookie", "Set-Cookie"], options.RedactedHeaders);
        Assert.Empty(options.RedactedQueryParameters);
        Assert.Empty(options.RedactedJsonFields);
        Assert.Equal("[REDACTED]", options.RedactionText);
    }

    [Fact]
    public void Custom_options_are_registered_and_used()
    {
        var services = new ServiceCollection();

        services.AddDebugProbe(options =>
        {
            options.MaxEntries = 2;
            options.MaxBodyCaptureSizeKb = 4;
            options.AllowLocalCompareTargets = true;
            options.AuthorizationPolicy = "DebugProbePolicy";
            options.IgnorePaths = ["/health"];
            options.CaptureOutgoingHttpClientRequests = false;
            options.ServerUrl = "https://debugprobe.example";
            options.ApplicationId = "sample-api";
            options.ApplicationName = "Sample API";
            options.InstanceId = "local-dev";
            options.RedactedHeaders = ["X-Api-Key"];
            options.RedactedQueryParameters = ["token"];
            options.RedactedJsonFields = ["password"];
            options.RedactionText = "***";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DebugProbeOptions>();
        var store = provider.GetRequiredService<DebugEntryStore>();

        Assert.Equal(2, options.MaxEntries);
        Assert.Equal(4, options.MaxBodyCaptureSizeKb);
        Assert.True(options.AllowLocalCompareTargets);
        Assert.Equal("DebugProbePolicy", options.AuthorizationPolicy);
        Assert.Equal(["/health"], options.IgnorePaths);
        Assert.False(options.CaptureOutgoingHttpClientRequests);
        Assert.Equal("https://debugprobe.example", options.ServerUrl);
        Assert.Equal("sample-api", options.ApplicationId);
        Assert.Equal("Sample API", options.ApplicationName);
        Assert.Equal("local-dev", options.InstanceId);
        Assert.Equal(["X-Api-Key"], options.RedactedHeaders);
        Assert.Equal(["token"], options.RedactedQueryParameters);
        Assert.Equal(["password"], options.RedactedJsonFields);
        Assert.Equal("***", options.RedactionText);
        Assert.NotNull(store.Environment);
    }
    [Fact]
    public void MaxEntries_zero_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDebugProbe(options =>
            {
                options.MaxEntries = 0;
            }));

        Assert.Contains("MaxEntries", exception.Message);
    }

    [Fact]
    public void MaxEntries_negative_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDebugProbe(options =>
            {
                options.MaxEntries = -1;
            }));

        Assert.Contains("MaxEntries", exception.Message);
    }

    [Fact]
    public void Invalid_ServerUrl_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddDebugProbe(options =>
            {
                options.ServerUrl = "localhost:5000";
            }));

        Assert.Contains("ServerUrl", exception.Message);
    }

    [Fact]
    public void MaxBodyCaptureSizeKb_negative_throws_ArgumentOutOfRangeException()
    {
        var options = new DebugProbeOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxBodyCaptureSizeKb = -1);
    }

    [Fact]
    public void MaxBodyCaptureSizeKb_zero_is_valid()
    {
        var options = new DebugProbeOptions();
        options.MaxBodyCaptureSizeKb = 0;

        Assert.Equal(0, options.MaxBodyCaptureSizeKb);
    }
}
