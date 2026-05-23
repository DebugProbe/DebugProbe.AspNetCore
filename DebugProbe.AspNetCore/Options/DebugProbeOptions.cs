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
    public int MaxBodyCaptureSizeKb { get; set; } = 256;

    /// <summary>
    /// Allows compare requests to local or private network targets.
    /// </summary>
    public bool AllowLocalCompareTargets { get; set; }

    /// <summary>
    /// Additional request paths to ignore.
    /// </summary>
    public string[] IgnorePaths { get; set; } = [];
}
