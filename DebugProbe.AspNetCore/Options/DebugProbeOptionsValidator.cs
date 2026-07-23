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

        if (options.AllowRedactionPreview && options.AllowUiInProduction)
        {
            return ValidateOptionsResult.Fail(
                "DebugProbe configuration is invalid. " +
                "AllowRedactionPreview = true combined with AllowUiInProduction = true is not allowed. " +
                "Enabling both could expose pre-redaction secret values through the UI in a production environment. " +
                "Either disable AllowRedactionPreview or keep AllowUiInProduction = false.");
        }

        return ValidateOptionsResult.Success;
    }
}