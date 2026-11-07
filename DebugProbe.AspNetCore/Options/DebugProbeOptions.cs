namespace DebugProbe.AspNetCore.Options;

/// <summary>Configuration options for DebugProbe.</summary>
public class DebugProbeOptions
{
    /// <summary>Enables request capture and DebugProbe endpoints.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional server export. Null keeps all telemetry local, as before.</summary>
    public DebugProbeServerOptions? Server { get; set; }

    /// <summary>Maximum number of requests kept in memory.</summary>
    public int MaxRequests { get; set; } = 20;

    /// <summary>Maximum captured size of each body in kilobytes. Zero disables body capture.</summary>
    public int MaxBodySizeKb { get; set; } = 32;

    internal int MaxBodySizeBytes => checked(MaxBodySizeKb * 1024);

    /// <summary>Captures incoming and outgoing request bodies.</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Captures incoming and outgoing response bodies.</summary>
    public bool CaptureResponseBody { get; set; } = true;

    /// <summary>Duration at which requests are marked slow. Zero disables the badge.</summary>
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Allows compare operations against localhost and private networks. Null allows them only in Development.</summary>
    public bool? AllowLocalCompareTargets { get; set; }

    /// <summary>Allows dashboard and API endpoints in Production. Capture remains enabled independently.</summary>
    public bool AllowUiInProduction { get; set; }

    /// <summary>Optional authorization policy required for all DebugProbe endpoints.</summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>Captures outgoing requests made through IHttpClientFactory during an incoming request.</summary>
    public bool CaptureOutgoingRequests { get; set; } = true;

    /// <summary>
    /// Paths excluded from capture, including child paths. Replace with an empty array to capture all application paths.
    /// DebugProbe's own endpoints are always excluded.
    /// </summary>
    public string[] ExcludedPaths { get; set; } =
    [
        "/swagger", "/health", "/healthz", "/ready", "/live", "/.well-known",
        "/favicon.ico", "/.git", "/wp-admin", "/phpmyadmin", "/cgi-bin", "/server-status"
    ];

    /// <summary>Header names whose values are replaced with [REDACTED]. Matching is case-insensitive.</summary>
    public string[] RedactedHeaders { get; set; } = ["Authorization", "Cookie", "Set-Cookie"];

    /// <summary>Query parameter names whose values are replaced with [REDACTED].</summary>
    public string[] RedactedQueryParameters { get; set; } = [];

    /// <summary>JSON property names whose values are replaced with [REDACTED].</summary>
    public string[] RedactedJsonFields { get; set; } = [];

    /// <summary>Route prefix for all DebugProbe endpoints. A leading slash is optional.</summary>
    public string RoutePrefix { get; set; } = "/debug";
}
