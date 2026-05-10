using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using DebugProbe.AspNetCore.Internal;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace DebugProbe.AspNetCore.Middleware;

/// <summary>
/// Middleware that captures HTTP request and response data and stores it
/// as DebugEntry for inspection via the DebugProbe UI.
/// </summary>
public class DebugProbeMiddleware
{
    private readonly RequestDelegate _next;

    public DebugProbeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, DebugEntryStore store)
    {
        var endpoint = context.GetEndpoint();

        var isApiEndpoint =
            endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null;

        if (!isApiEndpoint)
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();

        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var originalBody = context.Response.Body;
        using var ms = new MemoryStream();
        context.Response.Body = ms;

        var started = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            started.Stop();

            ms.Position = 0;
            var responseBody = await new StreamReader(ms).ReadToEndAsync();
            ms.Position = 0;
            await ms.CopyToAsync(originalBody);

            context.Response.Body = originalBody;

            var shortDatePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
            var index = shortDatePattern.LastIndexOf('y');
            var dataFormat = index >= 0 ? shortDatePattern[..(index + 1)] : shortDatePattern;

            store.Add(new DebugEntry
            {
                Id = Guid.NewGuid().ToString(),

                // Environment
                Environment = EnvironmentUtils.TryGetEnvironment(),
                MachineName = Environment.MachineName,
                AssemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                TimeZone = TimeZoneInfo.Local.DisplayName,
                Culture = CultureInfo.CurrentCulture.Name,
                DecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
                DateFormat = dataFormat,

                // Overview
                Method = context.Request.Method,
                Path = context.Request.Path,
                Query = context.Request.QueryString.ToString(),
                StatusCode = context.Response.StatusCode,
                RequestTimeUtc = DateTime.UtcNow,
                DurationMs = started.ElapsedMilliseconds,
                RequestSize = Encoding.UTF8.GetByteCount(requestBody),
                ResponseSize = Encoding.UTF8.GetByteCount(responseBody),

                // Request
                RequestUrl = $"{context.Request.Scheme}://{context.Request.Host}" + 
                        $"{context.Request.Path}{context.Request.QueryString}",
                RequestBody = Trim(requestBody),


                // Response
                ResponseBody = Trim(responseBody),


                // Headers
                Headers = context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),


                // Other
                Timestamp = DateTime.UtcNow,
             
            });
        }
    }

    private string Trim(string value, int max = 2000)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}