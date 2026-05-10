namespace DebugProbe.AspNetCore.Options;

public class DebugProbeOptions
{
    public int MaxEntries { get; set; } = 20;

    public string[] IgnorePaths { get; set; } = [];
}