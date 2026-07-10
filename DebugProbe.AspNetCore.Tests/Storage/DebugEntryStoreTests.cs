using System.Linq;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Xunit;

namespace DebugProbe.AspNetCore.Tests.Storage;

public class DebugEntryStoreTests
{
    [Fact]
    public void Add_identical_type_and_message_increments_count()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry1 = new DebugEntry
        {
            Id = "1",
            ResponseBody = "System.NullReferenceException: Object reference not set to an instance of an object.\r\n   at Program.Main()"
        };
        var entry2 = new DebugEntry
        {
            Id = "2",
            ResponseBody = "System.NullReferenceException: Object reference not set to an instance of an object.\r\n   at Program.Main()"
        };

        store.Add(entry1);
        store.Add(entry2);

        Assert.Single(store.ExceptionGroups);
        var group = store.ExceptionGroups.Values.First();
        Assert.Equal("System.NullReferenceException", group.Type);
        Assert.Equal("Object reference not set to an instance of an object.", group.SampleMessage);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public void Add_different_messages_produces_separate_groups()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry1 = new DebugEntry
        {
            Id = "1",
            ResponseBody = "System.NullReferenceException: Object reference not set to an instance of an object.\r\n   at Program.Main()"
        };
        var entry2 = new DebugEntry
        {
            Id = "2",
            ResponseBody = "System.InvalidOperationException: Operation is not valid due to the current state of the object.\r\n   at Program.Main()"
        };

        store.Add(entry1);
        store.Add(entry2);

        Assert.Equal(2, store.ExceptionGroups.Count);
    }

    [Fact]
    public void Add_messages_differing_only_in_dynamic_values_groups_them()
    {
        var store = new DebugEntryStore(new DebugProbeOptions());
        var entry1 = new DebugEntry
        {
            Id = "1",
            ResponseBody = "System.InvalidOperationException: Order 12345 failed.\r\n   at Program.Main()"
        };
        var entry2 = new DebugEntry
        {
            Id = "2",
            ResponseBody = "System.InvalidOperationException: Order 67890 failed.\r\n   at Program.Main()"
        };

        store.Add(entry1);
        store.Add(entry2);

        Assert.Single(store.ExceptionGroups);
        var group = store.ExceptionGroups.Values.First();
        Assert.Equal("System.InvalidOperationException", group.Type);
        Assert.Equal(2, group.Count);
    }
}
