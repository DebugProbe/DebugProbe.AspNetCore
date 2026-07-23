using System.Net;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DebugProbe.AspNetCore.Tests.Storage;

public class PinTests
{
    // -----------------------------------------------------------------------
    // TryPin – basic toggle
    // -----------------------------------------------------------------------

    [Fact]
    public void TryPin_on_unpinned_entry_marks_it_pinned()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry = new DebugEntry { Id = "e1", IsPinned = false };
        store.Add(entry);

        var (success, isPinned, error) = store.TryPin("e1");

        Assert.True(success);
        Assert.True(isPinned);
        Assert.Null(error);
        Assert.True(store.Get("e1")!.IsPinned);
    }

    [Fact]
    public void TryPin_on_pinned_entry_unpins_it()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry = new DebugEntry { Id = "e1", IsPinned = false };
        store.Add(entry);
        store.TryPin("e1"); // pin it

        var (success, isPinned, error) = store.TryPin("e1"); // unpin it

        Assert.True(success);
        Assert.False(isPinned);
        Assert.Null(error);
        Assert.False(store.Get("e1")!.IsPinned);
    }

    [Fact]
    public void TryPin_unknown_id_returns_failure()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());

        var (success, _, error) = store.TryPin("does-not-exist");

        Assert.False(success);
        Assert.NotNull(error);
    }

    // -----------------------------------------------------------------------
    // Cap enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public void TryPin_rejects_when_cap_is_reached()
    {
        var options = new DebugProbeOptions { MaxEntries = 100 };
        var store = new DebugEntryStore(options);

        // Pin exactly MaxPinnedEntries entries
        for (int i = 0; i < DebugEntryStore.MaxPinnedEntries; i++)
        {
            var e = new DebugEntry { Id = $"e{i}" };
            store.Add(e);
            var (ok, _, _) = store.TryPin($"e{i}");
            Assert.True(ok, $"Expected pin of e{i} to succeed");
        }

        // The (MaxPinnedEntries+1)-th entry must be rejected
        var overflow = new DebugEntry { Id = "overflow" };
        store.Add(overflow);

        var (success, isPinned, error) = store.TryPin("overflow");

        Assert.False(success);
        Assert.False(isPinned);
        Assert.NotNull(error);
        Assert.Contains(DebugEntryStore.MaxPinnedEntries.ToString(), error);
    }

    // -----------------------------------------------------------------------
    // Eviction protection
    // -----------------------------------------------------------------------

    [Fact]
    public void Pinned_entries_are_not_evicted_when_capacity_is_exceeded()
    {
        // MaxEntries = 3: one pinned slot, two regular FIFO slots
        var options = new DebugProbeOptions { MaxEntries = 3 };
        var store = new DebugEntryStore(options);

        var pinned = new DebugEntry { Id = "pinned" };
        store.Add(pinned);
        store.TryPin("pinned");

        // Add 4 more — the FIFO queue will start evicting from position 1 onwards
        for (int i = 1; i <= 4; i++)
        {
            store.Add(new DebugEntry { Id = $"regular-{i}" });
        }

        var all = store.GetAll();
        Assert.Contains(all, e => e.Id == "pinned");
        Assert.All(all.Where(e => e.Id != "pinned"), e => Assert.False(e.IsPinned));
    }

    // -----------------------------------------------------------------------
    // Clear() – full reset
    // -----------------------------------------------------------------------

    [Fact]
    public void Clear_removes_pinned_and_unpinned_entries()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry = new DebugEntry { Id = "e1" };
        store.Add(entry);
        store.TryPin("e1");

        store.Add(new DebugEntry { Id = "e2" });

        store.Clear();

        Assert.Empty(store.GetAll());
        Assert.Equal(0, store.PinnedCount);
    }

    // -----------------------------------------------------------------------
    // GetAll() ordering: pinned entries first
    // -----------------------------------------------------------------------

    [Fact]
    public void GetAll_returns_pinned_entries_first()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        store.Add(new DebugEntry { Id = "a" });
        store.Add(new DebugEntry { Id = "b" });
        store.Add(new DebugEntry { Id = "c" });
        store.TryPin("b"); // pin the middle one

        var all = store.GetAll();

        // Pinned entries must appear before unpinned ones
        var firstPinnedIndex = all.FindIndex(e => e.IsPinned);
        var firstUnpinnedIndex = all.FindIndex(e => !e.IsPinned);

        Assert.True(firstPinnedIndex < firstUnpinnedIndex,
            "Pinned entries should appear before unpinned entries in GetAll()");
    }
}
