namespace DebugProbe.AspNetCore.Options;

/// <summary>
/// Configuration options for DebugProbe.
/// </summary>
public class DebugProbeOptions
{
    /// <summary>
    /// Maximum number of stored entries.
    /// </summary>
    public int MaxEntries { get; set; } = 20;

    /// <summary>
    /// Maximum captured request or response body size in kilobytes.
    /// </summary>
    private int _maxBodyCaptureSizeKb = 32;
    public int MaxBodyCaptureSizeKb
    {
        get => _maxBodyCaptureSizeKb;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxBodyCaptureSizeKb),
                    "MaxBodyCaptureSizeKb must be 0 or greater. Use 0 to disable body capture.");
            _maxBodyCaptureSizeKb = value;
        }
    }

    internal int MaxBodyCaptureSizeBytes => MaxBodyCaptureSizeKb * 1024;

    /// <summary>
    /// Duration in milliseconds above which a request or outgoing dependency is flagged as slow in the UI. Set to 0 or negative to disable the badge.
    /// </summary>
    public int SlowRequestThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Lookback window in minutes for the request rate sparkline and error rate trend.
    /// Defaults to 30. Must be greater than or equal to 2.
    /// </summary>
    public int TrendLookbackMinutes { get; set; } = 2;

    /// <summary>
    /// Allows compare operations to target localhost and private network addresses.
    /// Defaults to true in Development and false in other environments unless explicitly configured.
    /// </summary>
    public bool? AllowLocalCompareTargets { get; set; }

    /// <summary>
    /// Allows DebugProbe UI endpoints to be registered in Production.
    /// Defaults to false.
    /// </summary>
    public bool AllowUiInProduction { get; set; }

    /// <summary>
    /// Optional ASP.NET Core authorization policy required for DebugProbe endpoints.
    /// When not configured, DebugProbe endpoints do not require authorization.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Captures outgoing requests made through IHttpClientFactory.
    /// Defaults to true.
    /// </summary>
    public bool CaptureOutgoingHttpClientRequests { get; set; } = true;

    /// <summary>
    /// Additional request paths to ignore.
    /// </summary>
    public string[] IgnorePaths { get; set; } = [];

    /// <summary>
    /// Header names whose values should be redacted before traces are stored.
    /// </summary>
    public string[] RedactedHeaders { get; set; } =
    [
        "Authorization",
        "Cookie",
        "Set-Cookie"
    ];

    /// <summary>
    /// Query parameter names whose values should be redacted before traces are stored.
    /// </summary>
    public string[] RedactedQueryParameters { get; set; } = [];

    /// <summary>
    /// JSON property names whose values should be redacted before traces are stored.
    /// </summary>
    public string[] RedactedJsonFields { get; set; } = [];

    /// <summary>
    /// Value used when sensitive data is redacted.
    /// </summary>
    public string RedactionText { get; set; } = "[REDACTED]";

    /// <summary>
    /// The route prefix for the DebugProbe dashboard and API endpoints.
    /// Defaults to <c>"/debug"</c>. A leading slash is added automatically if omitted.
    /// </summary>
    private string _routePrefix = "/debug";
    public string RoutePrefix
    {
        get => _routePrefix;
        set => _routePrefix = string.IsNullOrWhiteSpace(value)
            ? "/debug"
            : "/" + value.TrimStart('/');
    }
}
