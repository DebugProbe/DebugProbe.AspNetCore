namespace DebugProbe.AspNetCore.Models;

public class DebugOutgoingRequest
{
    public string Method { get; set; } = default!;

    public string Url { get; set; } = default!;

    public int? StatusCode { get; set; }

    public long DurationMs { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }

    public string? Exception { get; set; }

    public Dictionary<string, string> RequestHeaders { get; set; } = [];

    public Dictionary<string, string> ResponseHeaders { get; set; } = [];

    public DateTime TimestampUtc { get; set; }

    public bool IsSuccessStatusCode { get; set; }
}
