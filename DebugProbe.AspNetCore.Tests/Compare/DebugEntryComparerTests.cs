using System.Text.Json;
using DebugProbe.AspNetCore.Internal.Compare;
using DebugProbe.AspNetCore.Models;

namespace DebugProbe.AspNetCore.Tests.Compare;

public class DebugEntryComparerTests
{
    [Fact]
    public void Compare_reports_status_and_json_body_differences()
    {
        var local = new DebugEntry
        {
            StatusCode = 200,
            RequestBody = "{\"id\":1}",
            ResponseBody = "{\"total\":10,\"items\":[\"a\"]}"
        };
        var remote = new DebugEntry
        {
            StatusCode = 500,
            RequestBody = "{\"id\":2}",
            ResponseBody = "{\"total\":12,\"items\":[\"a\",\"b\"]}"
        };

        var json = JsonSerializer.Serialize(DebugEntryComparer.Compare(local, remote));

        Assert.Contains("\"field\":\"Status\"", json);
        Assert.Contains("\"field\":\"id\"", json);
        Assert.Contains("\"field\":\"total\"", json);
        Assert.Contains("\"field\":\"items\"", json);
    }
}
