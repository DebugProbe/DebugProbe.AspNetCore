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
        var originalBody = context.Response.Body;
        BoundedResponseCaptureStream? responseCapture = null;
        DebugEntry? entry = null;
        var requestBody = string.Empty;
        var maxBodySize = 0;

        try
        {
            if (_options.Enabled && !IsIgnoredPath(context.Request.Path))
            {
                maxBodySize = _options.MaxBodySizeBytes;
                requestBody = _options.CaptureRequestBody && maxBodySize > 0
                    ? await CaptureRequestBodyAsync(context, maxBodySize)
                    : string.Empty;
                responseCapture = new BoundedResponseCaptureStream(originalBody,
                    _options.CaptureResponseBody && maxBodySize > 0 ? maxBodySize + 1 : 0);
                entry = new DebugEntry { Id = Guid.NewGuid().ToString(), Timestamp = DateTime.UtcNow };
                context.Response.Body = responseCapture;
                context.Items["DebugProbeEntry"] = entry;
            }
        }
        catch (Exception)
        {
            // Capture is best-effort; setup failures must not prevent the application from running.
            context.Response.Body = originalBody;
            responseCapture?.Dispose();
            responseCapture = null;
        }

        if (responseCapture == null || entry == null)
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.StartNew();

        Exception? applicationException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            applicationException = ex;
            throw;
        }
        finally
        {
            started.Stop();

            var durationMs = started.ElapsedTicks > 0 ? Math.Max(1, started.ElapsedMilliseconds) : 0;

            context.Response.Body = originalBody;

            try
            {
                context.Items.Remove("DebugProbeEntry");
                var statusCode = applicationException != null && context.Response.StatusCode == 200 ? 500 : context.Response.StatusCode;

                var responseBody = !_options.CaptureResponseBody || maxBodySize == 0
                    ? string.Empty
                    : applicationException != null
                    ? HttpContentUtils.Trim(applicationException.ToString(), maxBodySize)
                    : CaptureResponseBody(context, responseCapture, maxBodySize);

                entry.Method = context.Request.Method;

                entry.Path = context.Request.Path;

                entry.Query = RedactionUtils.RedactQueryString(context.Request.QueryString.ToString(), _options);

                entry.StatusCode = statusCode;

                entry.RequestTimeUtc = DateTime.UtcNow;

                entry.DurationMs = durationMs;

                entry.RequestSize = context.Request.ContentLength ?? Encoding.UTF8.GetByteCount(requestBody);

                entry.ResponseSize = responseCapture.TotalBytesWritten;

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
            catch (Exception)
            {
                // Never replace the application response or its original exception with a capture failure.
            }
            finally
            {
                responseCapture.Dispose();
            }
        }
    }

    private bool IsIgnoredPath(PathString requestPath)
    {
        var path = requestPath.Value ?? string.Empty;

        return _options.ExcludedPaths
            .Append(_options.RoutePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(ignorePath => 
                path.Equals(ignorePath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ignorePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
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

        var originalPosition = context.Request.Body.Position;
        try
        {
            var bytes = await ReadAtMostAsync(context.Request.Body, maxBodySize + 1);
            return bytes.Length > maxBodySize ? BodyTooLargeMessage : Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            context.Request.Body.Position = originalPosition;
        }
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
