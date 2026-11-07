using System.Diagnostics;
using System.Text;
using DebugProbe.AspNetCore.Internal.Streams;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DebugProbe.AspNetCore.Middleware;

/// <summary>
/// Middleware that captures HTTP request and response data and stores it
/// as DebugEntry for inspection via the DebugProbe UI.
/// </summary>
public class DebugProbeMiddleware
{
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
                    ? CaptureExceptionBody(applicationException)
                    : CaptureResponseBody(context, responseCapture);

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

                entry.RequestBody = requestBody;

                entry.ResponseBody = responseBody;

                entry.ResponseHeaders =
                    context.Response.Headers.ToDictionary(
                        x => x.Key,
                        x => RedactionUtils.RedactHeader(x.Key, x.Value.ToString(), _options));

                entry.ExceptionType = applicationException?.GetType().FullName;
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

    private async Task<string> CaptureRequestBodyAsync(HttpContext context, int maxBodySize)
    {
        if (!HasBody(context.Request))
        {
            return string.Empty;
        }

        if (!HttpContentUtils.IsTextContent(context.Request.ContentType))
        {
            return context.Request.ContentLength == 0 ? string.Empty : BodyCaptureUtils.BinaryBodyMessage;
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
            
            return BodyCaptureUtils.Format(bytes, bytes.Length, context.Request.ContentType, _options);
        }
        finally
        {
            context.Request.Body.Position = originalPosition;
        }
    }

    private string CaptureResponseBody(HttpContext context, BoundedResponseCaptureStream responseCapture)
    {
        return BodyCaptureUtils.Format(responseCapture.CapturedBytes, responseCapture.TotalBytesWritten,
            context.Response.ContentType, _options);
    }

    private string CaptureExceptionBody(Exception exception)
    {
        var bytes = Encoding.UTF8.GetBytes(exception.ToString());
       
        return BodyCaptureUtils.Format(bytes, bytes.Length, "text/plain; charset=utf-8", _options);
    }

    private static bool HasBody(HttpRequest request)
    {
        var detection = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>();
        if (detection != null)
            return detection.CanHaveBody;

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
