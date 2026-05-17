using System.Diagnostics;
using System.Text;
using DebugProbe.AspNetCore.Internal.Streams;
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
    private const string BodyTooLargeMessage = "[Body too large]";
    private const string BinaryBodyMessage = "[Body not captured: non-text content]";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie"
    };

    private readonly RequestDelegate _next;
    private readonly DebugProbeOptions _options;

    public DebugProbeMiddleware(RequestDelegate next, DebugProbeOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task Invoke(HttpContext context, DebugEntryStore store)
    {
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

        var maxBodySize = _options.MaxBodyCaptureSizeKb * 1024;

        var requestBody = await CaptureRequestBodyAsync(context, maxBodySize);

        var originalBody = context.Response.Body;
        await using var responseCapture = new BoundedResponseCaptureStream(originalBody, maxBodySize + 1);
        context.Response.Body = responseCapture;

        var started = Stopwatch.StartNew();

        var exception = false;
        string? exceptionResponseBody = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = true;
            exceptionResponseBody = ex.ToString();
            throw;
        }
        finally
        {
            started.Stop();

            context.Response.Body = originalBody;

            var statusCode = exception && context.Response.StatusCode == 200 ? 500 : context.Response.StatusCode;
            var responseBody = exception ? Trim(exceptionResponseBody, maxBodySize) : CaptureResponseBody(context, responseCapture, maxBodySize);

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
                RequestSize = context.Request.ContentLength ?? Encoding.UTF8.GetByteCount(requestBody),
                ResponseSize = Encoding.UTF8.GetByteCount(responseBody),

                // Request
                RequestUrl = $"{context.Request.Scheme}://{context.Request.Host}" + 
                        $"{context.Request.Path}{context.Request.QueryString}",
                RequestBody = Trim(requestBody, maxBodySize),

                // Response
                ResponseBody = Trim(responseBody, maxBodySize),

                // Headers
                Headers = context.Request.Headers.ToDictionary(
                    x => x.Key,
                    x => SensitiveHeaders.Contains(x.Key) ? "[REDACTED]" : x.Value.ToString()),

                // Other
                Timestamp = DateTime.UtcNow,
            });
        }
    }

    private static async Task<string> CaptureRequestBodyAsync(HttpContext context, int maxBodySize)
    {
        if (!HasBody(context.Request))
        {
            return string.Empty;
        }

        if (!IsTextContent(context.Request.ContentType))
        {
            return BinaryBodyMessage;
        }

        if (context.Request.ContentLength > maxBodySize)
        {
            return BodyTooLargeMessage;
        }

        context.Request.EnableBuffering();

        if (!context.Request.Body.CanSeek)
        {
            return string.Empty;
        }

        context.Request.Body.Position = 0;
        var bytes = await ReadAtMostAsync(context.Request.Body, maxBodySize + 1);
        context.Request.Body.Position = 0;

        return bytes.Length > maxBodySize
            ? BodyTooLargeMessage
            : Encoding.UTF8.GetString(bytes);
    }

    private static string CaptureResponseBody(HttpContext context, BoundedResponseCaptureStream responseCapture, int maxBodySize)
    {
        if (!IsTextContent(context.Response.ContentType))
        {
            return responseCapture.TotalBytesWritten == 0
                ? string.Empty
                : BinaryBodyMessage;
        }

        if (responseCapture.TotalBytesWritten > maxBodySize)
        {
            return BodyTooLargeMessage;
        }

        return Encoding.UTF8.GetString(responseCapture.CapturedBytes);
    }

    private static bool HasBody(HttpRequest request)
    {
        if (request.ContentLength == 0)
        {
            return false;
        }

        if (request.ContentLength is > 0)
        {
            return true;
        }

        return string.Equals(request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(request.Method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(request.Method, HttpMethods.Patch, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextContent(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int byteLimit)
    {
        using var buffer = new MemoryStream();
        var remaining = byteLimit;
        var chunk = new byte[Math.Min(81920, byteLimit)];

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)));

            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
            remaining -= read;
        }

        return buffer.ToArray();
    }

    private static string Trim(string? value, int max = 2000)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
