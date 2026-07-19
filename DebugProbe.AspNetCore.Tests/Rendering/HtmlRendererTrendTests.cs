using System;
using System.Collections.Generic;
using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using Microsoft.Extensions.Options;
using Xunit;
using DebugProbe.AspNetCore.Storage;

namespace DebugProbe.AspNetCore.Tests.Rendering;

public class HtmlRendererTrendTests
{
    [Fact]
    public void Render_index_page_with_no_data_generates_flat_sparkline()
    {
        var html = HtmlRenderer.RenderIndexPage([], new DebugProbeOptions { TrendLookbackMinutes = 30 });

        // Flat sparkline check: Y values should all be 14
        Assert.Contains("14", html);
        Assert.Contains("trend-neutral", html);
        Assert.Contains("→", html);
    }

    [Fact]
    public void Render_index_page_with_increased_errors_shows_trend_up()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DebugProbeOptions { TrendLookbackMinutes = 10 };

        var items = new List<DebugEntry>
        {
            // Preceding half: [now - 10m, now - 5m). 1 request, 0 errors -> 0% error rate.
            new() { Id = "1", Timestamp = now.AddMinutes(-7), StatusCode = 200, Method = "GET", Path = "/api" },
            // Current half: [now - 5m, now]. 1 request, 1 error -> 100% error rate.
            new() { Id = "2", Timestamp = now.AddMinutes(-2), StatusCode = 500, Method = "GET", Path = "/api" }
        };

        var html = HtmlRenderer.RenderIndexPage(items, options);

        Assert.Contains("trend-up", html);
        Assert.Contains("↑", html);
    }

    [Fact]
    public void Render_index_page_with_decreased_errors_shows_trend_down()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DebugProbeOptions { TrendLookbackMinutes = 10 };

        var items = new List<DebugEntry>
        {
            // Preceding half: [now - 10m, now - 5m). 1 request, 1 error -> 100% error rate.
            new() { Id = "1", Timestamp = now.AddMinutes(-7), StatusCode = 500, Method = "GET", Path = "/api" },
            // Current half: [now - 5m, now]. 1 request, 0 errors -> 0% error rate.
            new() { Id = "2", Timestamp = now.AddMinutes(-2), StatusCode = 200, Method = "GET", Path = "/api" }
        };

        var html = HtmlRenderer.RenderIndexPage(items, options);

        Assert.Contains("trend-down", html);
        Assert.Contains("↓", html);
    }

    [Fact]
    public void Render_index_page_with_unchanged_errors_shows_trend_neutral()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new DebugProbeOptions { TrendLookbackMinutes = 10 };

        var items = new List<DebugEntry>
        {
            // Preceding half: [now - 10m, now - 5m). 1 request, 0 errors -> 0% error rate.
            new() { Id = "1", Timestamp = now.AddMinutes(-7), StatusCode = 200, Method = "GET", Path = "/api" },
            // Current half: [now - 5m, now]. 1 request, 0 errors -> 0% error rate.
            new() { Id = "2", Timestamp = now.AddMinutes(-2), StatusCode = 200, Method = "GET", Path = "/api" }
        };

        var html = HtmlRenderer.RenderIndexPage(items, options);

        Assert.Contains("trend-neutral", html);
        Assert.Contains("→", html);
    }

    [Fact]
    public void Render_index_page_scenario_a_and_b_regression_test()
    {
        var options = new DebugProbeOptions { MaxEntries = 10, TrendLookbackMinutes = 2 };

        var now = DateTimeOffset.UtcNow;

        // Scenario A: 8 successes at t = now - 90s, then 5 errors at t = now - 5s
        // Since MaxEntries = 10, 3 successes are evicted. 5 successes remain in previous window, 5 errors in current.
        // TotalB = 5 > 0, TotalA = 5 > 0.
        var itemsA = new List<DebugEntry>();
        for (int i = 0; i < 5; i++)
        {
            itemsA.Add(new DebugEntry { Id = $"success-{i}", Timestamp = now.AddSeconds(-90), StatusCode = 200, Method = "GET", Path = "/api" });
        }
        for (int i = 0; i < 5; i++)
        {
            itemsA.Add(new DebugEntry { Id = $"error-{i}", Timestamp = now.AddSeconds(-5), StatusCode = 500, Method = "GET", Path = "/api" });
        }

        // Expected: should show Trend Up (↑) because previous window is valid and error rate went up from 0% to 100%
        var htmlA = HtmlRenderer.RenderIndexPage(itemsA, options);
        Assert.Contains("trend-up", htmlA);
        Assert.Contains("↑", htmlA);

        // Scenario B: 8 recovery successes are sent, evicting all baseline successes and some errors.
        // Queue now has 8 recovery successes and 2 errors (all in current window).
        // Previous window is empty (TotalB = 0).
        var itemsB = new List<DebugEntry>();
        for (int i = 0; i < 2; i++)
        {
            itemsB.Add(new DebugEntry { Id = $"error-{i}", Timestamp = now.AddSeconds(-5), StatusCode = 500, Method = "GET", Path = "/api" });
        }
        for (int i = 0; i < 8; i++)
        {
            itemsB.Add(new DebugEntry { Id = $"recovery-{i}", Timestamp = now.AddSeconds(-2), StatusCode = 200, Method = "GET", Path = "/api" });
        }

        // Expected: should show Trend Neutral (→) because previous window is empty
        var htmlB = HtmlRenderer.RenderIndexPage(itemsB, options);
        Assert.Contains("trend-neutral", htmlB);
        Assert.Contains("→", htmlB);
    }

    [Fact]
    public void Render_index_page_previous_window_evicted_shows_neutral()
    {
        var options = new DebugProbeOptions { MaxEntries = 10, TrendLookbackMinutes = 2 };
        var items = new List<DebugEntry>();

        var now = DateTimeOffset.UtcNow;
        // All baseline entries from previous window are evicted. Previous window has 0 entries.
        for (int i = 0; i < 10; i++)
        {
            items.Add(new DebugEntry { Id = $"recovery-{i}", Timestamp = now.AddSeconds(-2), StatusCode = 200, Method = "GET", Path = "/api" });
        }

        var html = HtmlRenderer.RenderIndexPage(items, options);
        Assert.Contains("trend-neutral", html);
        Assert.Contains("→", html);
    }

    [Fact]
    public void Render_index_page_previous_window_no_traffic_shows_neutral()
    {
        var options = new DebugProbeOptions { MaxEntries = 100, TrendLookbackMinutes = 2 };
        var items = new List<DebugEntry>();

        var now = DateTimeOffset.UtcNow;
        // Only 5 errors are sent now. No traffic occurred in the previous window.
        for (int i = 0; i < 5; i++)
        {
            items.Add(new DebugEntry { Id = $"error-{i}", Timestamp = now.AddSeconds(-5), StatusCode = 500, Method = "GET", Path = "/api" });
        }

        var html = HtmlRenderer.RenderIndexPage(items, options);
        Assert.Contains("trend-neutral", html);
        Assert.Contains("→", html);
    }

    [Fact]
    public void Render_index_page_happy_path_with_no_eviction_flips_trend()
    {
        var options = new DebugProbeOptions { MaxEntries = 100, TrendLookbackMinutes = 2 };
        var store = new DebugEntryStore(options);

        var now = DateTimeOffset.UtcNow;
        // Phase 1: 8 successes at t = now - 90s (previous window)
        for (int i = 0; i < 8; i++)
        {
            store.Add(new DebugEntry { Id = $"success-{i}", Timestamp = now.AddSeconds(-90), StatusCode = 200, Method = "GET", Path = "/api" });
        }

        // Phase 2: 5 errors at t = now - 5s (current window)
        for (int i = 0; i < 5; i++)
        {
            store.Add(new DebugEntry { Id = $"error-{i}", Timestamp = now.AddSeconds(-5), StatusCode = 500, Method = "GET", Path = "/api" });
        }

        var htmlErrors = HtmlRenderer.RenderIndexPage(store.GetAll(), options);
        Assert.Contains("trend-up", htmlErrors);
        Assert.Contains("↑", htmlErrors);

        store.Clear();

        // Phase 3: 5 errors at t = now - 90s (previous window)
        for (int i = 0; i < 5; i++)
        {
            store.Add(new DebugEntry { Id = $"error-{i}", Timestamp = now.AddSeconds(-90), StatusCode = 500, Method = "GET", Path = "/api" });
        }

        // Phase 4: 8 successes at t = now - 5s (current window)
        for (int i = 0; i < 8; i++)
        {
            store.Add(new DebugEntry { Id = $"success-{i}", Timestamp = now.AddSeconds(-5), StatusCode = 200, Method = "GET", Path = "/api" });
        }

        var htmlRecovery = HtmlRenderer.RenderIndexPage(store.GetAll(), options);
        Assert.Contains("trend-down", htmlRecovery);
        Assert.Contains("↓", htmlRecovery);
    }

    [Fact]
    public void Options_validator_rejects_trend_lookback_less_than_two()
    {
        var validator = new DebugProbeOptionsValidator();
        var options = new DebugProbeOptions { TrendLookbackMinutes = 1 };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("TrendLookbackMinutes must be greater than or equal to 2", result.FailureMessage);
    }
}
