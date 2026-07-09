using System;

namespace DebugProbe.AspNetCore.Models;

/// <summary>
/// Represents a grouped set of identical exceptions.
/// </summary>
public class ExceptionGroup
{
    /// <summary>
    /// Gets or sets the fingerprint (hash) of the exception group.
    /// </summary>
    public string Fingerprint { get; set; } = default!;

    /// <summary>
    /// Gets or sets the type of the exception.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Gets or sets a sample message from the exception.
    /// </summary>
    public string SampleMessage { get; set; } = default!;

    /// <summary>
    /// Gets or sets the count of exceptions in this group.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the exception was last seen.
    /// </summary>
    public DateTimeOffset LastSeen { get; set; }
}
