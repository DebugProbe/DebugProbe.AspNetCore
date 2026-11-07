namespace DebugProbe.AspNetCore.Options;

/// <summary>Optional, best-effort export to a DebugProbe Server.</summary>
public sealed class DebugProbeServerOptions
{
    /// <summary>Server base URL, for example http://localhost:18080. May include a path prefix.</summary>
    public string Url { get; set; } = "";

    /// <summary>Ingestion key issued by the server for this application and environment.</summary>
    public string ApiKey { get; set; } = "";
    
    /// <summary>Application ID matching the ingestion key.</summary>
    public string ApplicationId { get; set; } = "";

    /// <summary>Display name. Defaults to the host application's name.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>Environment matching the key. Null uses the host environment; empty means unnamed.</summary>
    public string? Environment { get; set; }

    /// <summary>Maximum queued events. Full queues drop new events without blocking requests.</summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Maximum time for one HTTP send.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Interval between application heartbeats.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);
}
