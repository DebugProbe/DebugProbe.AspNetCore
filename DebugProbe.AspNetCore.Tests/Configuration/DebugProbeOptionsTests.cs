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
        Assert.False(options.AllowLocalCompareTargets);
        Assert.Empty(options.IgnorePaths);
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
            options.IgnorePaths = ["/health"];
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DebugProbeOptions>();
        var store = provider.GetRequiredService<DebugEntryStore>();

        Assert.Equal(2, options.MaxEntries);
        Assert.Equal(4, options.MaxBodyCaptureSizeKb);
        Assert.True(options.AllowLocalCompareTargets);
        Assert.Equal(["/health"], options.IgnorePaths);
        Assert.NotNull(store.Environment);
    }
}
