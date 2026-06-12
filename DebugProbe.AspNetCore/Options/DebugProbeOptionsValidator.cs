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

        return ValidateOptionsResult.Success;
    }
}