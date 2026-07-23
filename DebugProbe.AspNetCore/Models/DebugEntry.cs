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

    /// <summary>
    /// Whether this entry is pinned (protected from FIFO eviction).
    /// Resets to false on application restart — entirely in-memory, no persistence.
    /// </summary>
    public bool IsPinned { get; set; }

    // -----------------------------------------------------------------------
    // Redaction preview fields (only populated when AllowRedactionPreview = true)
    // These hold pre-redaction values for local reveal-only display on the detail page.
    // They are NEVER serialised to the JSON export endpoint.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Original (pre-redaction) request headers. Populated only when
    /// <see cref="DebugProbe.AspNetCore.Options.DebugProbeOptions.AllowRedactionPreview"/> is <c>true</c>.
    /// </summary>
    public Dictionary<string, string> OriginalRequestHeaders { get; set; } = new();

    /// <summary>
    /// Original (pre-redaction) request body.
    /// </summary>
    public string? OriginalRequestBody { get; set; }

    /// <summary>
    /// Original (pre-redaction) response body.
    /// </summary>
    public string? OriginalResponseBody { get; set; }

    /// <summary>
    /// Original (pre-redaction) query string.
    /// </summary>
    public string? OriginalQuery { get; set; }

    public List<DebugOutgoingRequest> OutgoingRequests { get; set; } = [];
}
