using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace DebugProbe.AspNetCore.Tests.Rendering;

public class RendererIsolationTests
{
    [Fact]
    public void Renderer_uses_only_explicitly_supplied_groups()
    {
        var first = new DebugEntryStore(new DebugProbeOptions());
        first.Add(new DebugEntry { Id = "first", ResponseBody = "System.Exception: first application" });
        var second = new DebugEntryStore(new DebugProbeOptions());
        second.Add(new DebugEntry { Id = "second", ResponseBody = "System.Exception: second application" });

        var firstHtml = HtmlRenderer.RenderIndexPage([], exceptionGroups: first.ExceptionGroups.Values.ToArray());
        var secondHtml = HtmlRenderer.RenderIndexPage([], exceptionGroups: second.ExceptionGroups.Values.ToArray());
        var emptyHtml = HtmlRenderer.RenderIndexPage([]);

        Assert.Contains("first application", firstHtml);
        Assert.DoesNotContain("second application", firstHtml);
        Assert.Contains("second application", secondHtml);
        Assert.DoesNotContain("first application", secondHtml);
        Assert.DoesNotContain("Exception Groups", emptyHtml);
        first.Clear();
        Assert.Contains("second application", HtmlRenderer.RenderIndexPage([], exceptionGroups: second.ExceptionGroups.Values.ToArray()));
    }

    [Fact]
    public async Task Two_running_applications_display_their_own_exception_groups()
    {
        await using var first = await DebugProbeWebApplication.CreateAsync(Environments.Development,
            endpoints => endpoints.MapGet("/failure", () => Results.Text("System.Exception: first application")));
        await using var second = await DebugProbeWebApplication.CreateAsync(Environments.Development,
            endpoints => endpoints.MapGet("/failure", () => Results.Text("System.Exception: second application")));

        await Task.WhenAll(first.Client.GetAsync("/failure"), second.Client.GetAsync("/failure"));
        var pages = await Task.WhenAll(first.Client.GetStringAsync("/debug"), second.Client.GetStringAsync("/debug"));

        Assert.Contains("first application", pages[0]);
        Assert.DoesNotContain("second application", pages[0]);
        Assert.Contains("second application", pages[1]);
        Assert.DoesNotContain("first application", pages[1]);
        await first.Client.PostAsync("/debug/clear", null);
        Assert.DoesNotContain("Exception Groups", await first.Client.GetStringAsync("/debug"));
        Assert.Contains("second application", await second.Client.GetStringAsync("/debug"));
    }
}
