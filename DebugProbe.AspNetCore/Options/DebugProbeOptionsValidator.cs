using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Options;

internal sealed class DebugProbeOptionsValidator
    : IValidateOptions<DebugProbeOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        DebugProbeOptions options)
    {
        if (options.MaxEntries < 1)
        {
            return ValidateOptionsResult.Fail(
                $"DebugProbe configuration is invalid. " +
                $"MaxEntries must be greater than or equal to 1. " +
                $"Provided value: {options.MaxEntries}.");
        }

        if (options.TrendLookbackMinutes < 2)
        {
            return ValidateOptionsResult.Fail(
                $"DebugProbe configuration is invalid. " +
                $"TrendLookbackMinutes must be greater than or equal to 2 (to allow splitting into two windows). " +
                $"Provided value: {options.TrendLookbackMinutes}.");
        }

        if (!string.IsNullOrWhiteSpace(options.ServerUrl) &&
            (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var serverUri) ||
             (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps)))
        {
            return ValidateOptionsResult.Fail(
                "DebugProbe configuration is invalid. " +
                "ServerUrl must be an absolute HTTP or HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}