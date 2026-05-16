namespace DebugProbe.AspNetCore.Options;

public class DebugProbeOptions
{
    public int MaxEntries { get; set; } = 20;

    public int MaxBodyCaptureSizeKb { get; set; } = 256;

    public bool AllowLocalCompareTargets { get; set; }

    public string[] IgnorePaths { get; set; } = [];

}
