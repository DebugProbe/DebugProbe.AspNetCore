using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Options;

internal sealed class DebugProbeOptionsValidator : IValidateOptions<DebugProbeOptions>
{
    public ValidateOptionsResult Validate(string? name, DebugProbeOptions options)
    {
        var errors = new List<string>();
        if (options.MaxRequests < 1)
            errors.Add("MaxRequests must be at least 1.");
        if (options.MaxBodySizeKb < 0 || options.MaxBodySizeKb > (int.MaxValue - 1) / 1024)
            errors.Add($"MaxBodySizeKb must be between 0 and {(int.MaxValue - 1) / 1024}.");
        if (options.SlowRequestThreshold < TimeSpan.Zero)
            errors.Add("SlowRequestThreshold must be zero or greater.");
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) || options.RoutePrefix == "/" ||
            options.RoutePrefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_')) ||
            options.RoutePrefix.Contains("//"))
            errors.Add("RoutePrefix must contain a non-empty path using letters, digits, '/', '-' or '_'.");
        if (options.ExcludedPaths is null || options.ExcludedPaths.Any(p => string.IsNullOrWhiteSpace(p) || !p.StartsWith('/')))
            errors.Add("ExcludedPaths must contain paths starting with '/'.");
        if (options.RedactedHeaders is null || options.RedactedQueryParameters is null || options.RedactedJsonFields is null)
            errors.Add("Redaction lists must not be null. Use an empty array instead.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
