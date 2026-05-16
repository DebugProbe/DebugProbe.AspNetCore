namespace DebugProbe.AspNetCore.Models;

public class DebugEntry
{
    public string Id { get; set; } = default!;

    // Request
    public string Path { get; set; } = default!;
    public string Method { get; set; } = default!;
    public string? Query { get; set; }
    public string? RequestUrl { get; set; }
    public string RequestBody { get; set; } = default!;
    public DateTimeOffset RequestTimeUtc { get; set; }
    public long RequestSize { get; set; }
    public long DurationMs { get; set; }

    // Response
    public int StatusCode { get; set; }
    public long ResponseSize { get; set; }
    public string ResponseBody { get; set; } = default!;

    // Headers
    public Dictionary<string, string> Headers { get; set; } = new();

    // Metadata
    public DateTimeOffset Timestamp { get; set; }
}
