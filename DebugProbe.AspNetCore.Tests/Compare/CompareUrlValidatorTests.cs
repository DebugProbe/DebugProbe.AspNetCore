using DebugProbe.AspNetCore.Internal.Compare;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Tests.Compare;

public class CompareUrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com:8080/path")]
    public async Task Validates_valid_http_and_https_urls(string url)
    {
        var result = await CompareUrlValidator.ValidateCompareBaseUrlAsync(url, new DebugProbeOptions());

        Assert.True(result.IsValid);
        Assert.NotNull(result.BaseUri);
        Assert.Equal(new Uri(url).GetLeftPart(UriPartial.Authority), result.BaseUri.ToString().TrimEnd('/'));
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///tmp/test")]
    public async Task Rejects_invalid_schemes(string url)
    {
        var result = await CompareUrlValidator.ValidateCompareBaseUrlAsync(url, new DebugProbeOptions());

        Assert.False(result.IsValid);
        Assert.Equal("Compare server URL must use http or https", result.Error);
    }

    [Fact]
    public async Task Rejects_localhost_by_default()
    {
        var result = await CompareUrlValidator.ValidateCompareBaseUrlAsync("http://localhost:5000", new DebugProbeOptions());

        Assert.False(result.IsValid);
        Assert.Equal("Compare server URL cannot target localhost", result.Error);
    }

    [Fact]
    public async Task Allows_localhost_when_local_compare_targets_are_enabled()
    {
        var result = await CompareUrlValidator.ValidateCompareBaseUrlAsync(
            "http://localhost:5000/debug",
            new DebugProbeOptions { AllowLocalCompareTargets = true });

        Assert.True(result.IsValid);
        Assert.Equal("http://localhost:5000", result.BaseUri?.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task Validates_allow_local_compare_targets_for_private_addresses()
    {
        var blocked = await CompareUrlValidator.ValidateCompareBaseUrlAsync(
            "http://127.0.0.1:5000",
            new DebugProbeOptions());
        var allowed = await CompareUrlValidator.ValidateCompareBaseUrlAsync(
            "http://127.0.0.1:5000",
            new DebugProbeOptions { AllowLocalCompareTargets = true });

        Assert.False(blocked.IsValid);
        Assert.True(allowed.IsValid);
    }
}
