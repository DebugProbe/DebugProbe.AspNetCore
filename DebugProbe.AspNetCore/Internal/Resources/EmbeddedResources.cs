namespace DebugProbe.AspNetCore.Internal.Resources;

/// <summary>
/// Provides access to embedded UI resources (HTML, CSS, JS) used by DebugProbe.
/// Resources are loaded once and reused to render the DebugProbe UI.
/// </summary>
internal static class EmbeddedResources
{
    public static readonly string Css = ResourceLoader.LoadCss("debugprobe.css");

    public static readonly string Layout = ResourceLoader.LoadHtml("_shared.layout.html");

    public static readonly string Index = ResourceLoader.LoadHtml("index.html");

    public static readonly string Details = ResourceLoader.LoadHtml("details.html");

    public static readonly Dictionary<string, string> JavaScript = new()
    {
        ["debugprobe-compare-renderer.js"] =
            ResourceLoader.LoadJs("debugprobe-compare-renderer.js"),

        ["debugprobe-compare-engine.js"] =
            ResourceLoader.LoadJs("debugprobe-compare-engine.js"),

        ["debugprobe-ui.js"] =
            ResourceLoader.LoadJs("debugprobe-ui.js")
    };
}