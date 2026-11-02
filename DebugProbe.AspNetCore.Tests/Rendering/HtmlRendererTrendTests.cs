using System;
using System.Collections.Generic;
using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using Microsoft.Extensions.Options;
using Xunit;
using DebugProbe.AspNetCore.Storage;

namespace DebugProbe.AspNetCore.Tests.Rendering;

public class HtmlRendererTrendTests
{
    [Fact]
    public void Options_validator_rejects_trend_lookback_less_than_two()
    {
        var validator = new DebugProbeOptionsValidator();
        var options = new DebugProbeOptions { TrendLookbackMinutes = 1 };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("TrendLookbackMinutes must be greater than or equal to 2", result.FailureMessage);
    }
}
