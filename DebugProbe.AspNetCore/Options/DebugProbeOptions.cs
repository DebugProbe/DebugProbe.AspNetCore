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
    public int MaxBodyCaptureSizeKb { get; set; } = 32;

    internal int MaxBodyCaptureSizeBytes => MaxBodyCaptureSizeKb * 1024;

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
}
