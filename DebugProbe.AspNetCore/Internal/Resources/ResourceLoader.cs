using System.Reflection;

namespace DebugProbe.AspNetCore.Internal.Resources;

/// <summary>
/// Loads embedded resources (HTML, CSS, JS) from the assembly.
/// </summary>
internal static class ResourceLoader
{
    private static readonly Assembly Assembly = typeof(ResourceLoader).Assembly;
    private const string Base = "DebugProbe.AspNetCore.Assets.";

    public static string LoadJs(string file)
        => Load("js", file);

    public static string LoadHtml(string file)
        => Load("html", file);

    public static string LoadCss(string file)
        => Load("css", file);

    private static string Load(string folder, string file)
    {
        var name = $"{Base}{folder}.{file}";

        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource not found: {name}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}