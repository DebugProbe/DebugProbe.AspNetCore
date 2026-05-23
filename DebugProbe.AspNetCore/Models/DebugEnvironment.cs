namespace DebugProbe.AspNetCore.Models;

public class DebugEnvironment
{
    public string Environment { get; init; } = default!;

    public string Culture { get; init; } = default!;

    public string? UiCulture { get; init; }

    public string? MachineName { get; init; }

    public string? AssemblyVersion { get; init; }

    public string? TimeZone { get; init; }

    public string? DecimalSeparator { get; init; }

    public string? DateFormat { get; init; }
}
