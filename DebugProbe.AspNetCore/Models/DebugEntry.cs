namespace DebugProbe.AspNetCore.Models;

public class DebugEntry
{
    public string Id { get; set; } = default!;

    public string Path { get; set; } = default!;

    public string Method { get; set; } = default!;

    public string? Query { get; set; }

    public string? RequestUrl { get; set; }

    public string RequestBody { get; set; } = default!;

    public DateTimeOffset RequestTimeUtc { get; set; }

    public long RequestSize { get; set; }

    public long DurationMs { get; set; }

    public int StatusCode { get; set; }

    public long ResponseSize { get; set; }

    public string ResponseBody { get; set; } = default!;

    public Dictionary<string, string> RequestHeaders { get; set; } = new();

    public Dictionary<string, string> ResponseHeaders { get; set; } = new();

    public DateTimeOffset Timestamp { get; set; }

    public bool IsPinned { get; set; }

    public List<DebugOutgoingRequest> OutgoingRequests { get; set; } = [];
}
