using System.Diagnostics;
using System.Text;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Middleware;

/// <summary>
/// Middleware that captures HTTP request and response data and stores it
/// as DebugEntry for inspection via the DebugProbe UI.
/// </summary>
public class DebugProbeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DebugProbeOptions _options;

    public DebugProbeMiddleware(RequestDelegate next, DebugProbeOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task Invoke(HttpContext context, DebugEntryStore store)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        var ignored =
        path.StartsWith("/debug") ||
        path.StartsWith("/swagger") ||
        _options.IgnorePaths.Any(x =>
            path.StartsWith(x, StringComparison.OrdinalIgnoreCase));

        if (ignored)
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

        var exception = false;
        string? exceptionMessage = null;
        string? exceptionStackTrace = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = true;
            exceptionMessage = ex.Message;
            exceptionStackTrace = ex.StackTrace;
            throw;
        }
        finally
        {
            started.Stop();

            ms.Position = 0;
            var responseBody = await new StreamReader(ms).ReadToEndAsync();
            ms.Position = 0;

            await ms.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            var statusCode = exception && context.Response.StatusCode == 200 ? 500 : context.Response.StatusCode;

            store.Add(new DebugEntry
            {
                Id = Guid.NewGuid().ToString(),

                // Overview
                Method = context.Request.Method,
                Path = context.Request.Path,
                Query = context.Request.QueryString.ToString(),
                StatusCode = statusCode,
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

                // Exception
                ExceptionMessage = exceptionMessage,
                ExceptionStackTrace = Trim(exceptionStackTrace, max: 4000),

                // Headers
                Headers = context.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),

                // Other
                Timestamp = DateTime.UtcNow,
            });
        }
    }

    private string Trim(string? value, int max = 2000)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
