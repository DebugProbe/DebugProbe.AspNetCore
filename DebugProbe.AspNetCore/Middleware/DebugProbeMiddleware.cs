using System.Diagnostics;
using System.Text;
using DebugProbe.AspNetCore.Internal.Streams;
using DebugProbe.AspNetCore.Internal.Utils;
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

    private static readonly string[] DefaultIgnorePaths =
    [
        "/debug",
        "/compare",
        "/swagger",
        "/.well-known",

        // browser noise
        "/favicon.ico",

        // scanners
        "/.git",
        "/wp-admin",
        "/phpmyadmin",
        "/cgi-bin",
        "/server-status"
    ];

    private readonly RequestDelegate _next;
    private readonly DebugProbeOptions _options;

    /// <summary>
    /// Initializes a new instance of the middleware.
    /// </summary>
    public DebugProbeMiddleware(RequestDelegate next, DebugProbeOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// Processes the current HTTP request.
    /// </summary>
    public async Task Invoke(HttpContext context, DebugEntryStore store)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        var ignorePaths = DefaultIgnorePaths
            .Concat(_options.IgnorePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var ignored = ignorePaths.Any(x =>
            path.StartsWith(x, StringComparison.OrdinalIgnoreCase));

        if (ignored)
        {
            await _next(context);
            return;
        }

        var maxBodySize = _options.MaxBodyCaptureSizeBytes;

        var requestBody = await CaptureRequestBodyAsync(context, maxBodySize);

        var originalBody = context.Response.Body;

        await using var responseCapture =
            new BoundedResponseCaptureStream(originalBody, maxBodySize + 1);

        context.Response.Body = responseCapture;

        var entry = new DebugEntry
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };

        context.Items["DebugProbeEntry"] = entry;

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

            var durationMs = started.ElapsedTicks > 0 ? Math.Max(1, started.ElapsedMilliseconds) : 0;

            context.Response.Body = originalBody;

            var statusCode = exception && context.Response.StatusCode == 200 ? 500 : context.Response.StatusCode;

            var responseBody = exception ? HttpContentUtils.Trim(exceptionResponseBody, maxBodySize) : CaptureResponseBody(context, responseCapture, maxBodySize);

            entry.Method = context.Request.Method;

            entry.Path = context.Request.Path;

            entry.Query = RedactionUtils.RedactQueryString(context.Request.QueryString.ToString(), _options);

            entry.StatusCode = statusCode;

            entry.RequestTimeUtc = DateTime.UtcNow;

            entry.DurationMs = durationMs;

            entry.RequestSize = context.Request.ContentLength ?? Encoding.UTF8.GetByteCount(requestBody);

            entry.ResponseSize = Encoding.UTF8.GetByteCount(responseBody);

            entry.RequestHeaders =
                context.Request.Headers.ToDictionary(
                    x => x.Key,
                    x => RedactionUtils.RedactHeader(x.Key, x.Value.ToString(), _options));

            entry.RequestUrl =
                RedactionUtils.RedactUrl(
                    $"{context.Request.Scheme}://{context.Request.Host}" +
                    $"{context.Request.Path}{context.Request.QueryString}",
                    _options);

            entry.RequestBody = RedactionUtils.RedactJsonFields(
                HttpContentUtils.Trim(requestBody, maxBodySize),
                _options);

            entry.ResponseBody = RedactionUtils.RedactJsonFields(
                HttpContentUtils.Trim(responseBody, maxBodySize),
                _options);

            entry.ResponseHeaders =
                context.Response.Headers.ToDictionary(
                    x => x.Key,
                    x => RedactionUtils.RedactHeader(x.Key, x.Value.ToString(), _options));

            store.Add(entry);
        }
    }

    private static async Task<string> CaptureRequestBodyAsync(HttpContext context, int maxBodySize)
    {
        if (!HasBody(context.Request))
        {
            return string.Empty;
        }

        if (!HttpContentUtils.IsTextContent(context.Request.ContentType))
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

        return bytes.Length > maxBodySize ? BodyTooLargeMessage : Encoding.UTF8.GetString(bytes);
    }

    private static string CaptureResponseBody(HttpContext context, BoundedResponseCaptureStream responseCapture, int maxBodySize)
    {
        if (!HttpContentUtils.IsTextContent(context.Response.ContentType))
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

    private static async Task<byte[]> ReadAtMostAsync(Stream stream, int byteLimit)
    {
        using var buffer = new MemoryStream();

        var remaining = byteLimit;

        var chunk = new byte[Math.Min(81920, byteLimit)];

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, Math.Min(chunk.Length, remaining)));

            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));

            remaining -= read;
        }

        return buffer.ToArray();
    }
}
