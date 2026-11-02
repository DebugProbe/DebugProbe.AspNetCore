using System.Diagnostics;
using System.Net;
using System.Text;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Handlers;

/// <summary>
/// Captures outgoing HttpClient requests and responses.
/// </summary>
public class DebugProbeHttpClientHandler : DelegatingHandler
{
    private readonly DebugProbeOptions _options;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public DebugProbeHttpClientHandler(IHttpContextAccessor httpContextAccessor, DebugProbeOptions options)
    {
        _httpContextAccessor = httpContextAccessor;

        _options = options;
    }

    /// <summary>
    /// Sends the HTTP request and captures tracing information.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            await TryCaptureRequest(request, null, ex, started.ElapsedMilliseconds, cancellationToken);
            throw;
        }

        await TryCaptureRequest(request, response, null, started.ElapsedMilliseconds, cancellationToken);
        return response;
    }

    /// <summary>
    /// Captures outgoing request details and stores them in the active DebugProbe entry.
    /// </summary>
    private async Task TryCaptureRequest(
        HttpRequestMessage request, HttpResponseMessage? response, Exception? exception,
        long durationMs, CancellationToken cancellationToken)
    {
        try
        {
            await CaptureRequest(request, response, exception, durationMs, cancellationToken);
        }
        catch (Exception)
        {
            // Diagnostics must not replace a successful response or the original transport exception.
        }
    }

    private async Task CaptureRequest(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception? exception,
        long durationMs,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.CaptureOutgoingRequests || !TryGetActiveEntry(out var entry))
        {
            return;
        }

        var outgoing = new DebugOutgoingRequest
        {
            Method = request.Method.Method,

            Url = RedactionUtils.RedactUrl(request.RequestUri?.ToString(), _options),

            StatusCode = response != null ? (int)response.StatusCode : null,

            DurationMs = durationMs,

            Exception = exception?.ToString(),

            TimestampUtc = DateTime.UtcNow,

            IsSuccessStatusCode = response?.IsSuccessStatusCode ?? false,

            RequestHeaders = request.Headers.ToDictionary(x => x.Key, x => RedactionUtils.RedactHeader(x.Key, string.Join(", ", x.Value), _options)),

            ResponseHeaders = response != null ? response.Headers.ToDictionary(x => x.Key, x => RedactionUtils.RedactHeader(x.Key, string.Join(", ", x.Value), _options)) : []
        };

        outgoing.RequestBody = _options.CaptureRequestBody
            ? await CaptureBodyAsync(request.Content, content => request.Content = content, cancellationToken)
            : string.Empty;

        outgoing.ResponseBody = _options.CaptureResponseBody
            ? await CaptureResponseBodyAsync(response, cancellationToken)
            : string.Empty;

        entry.OutgoingRequests.Enqueue(outgoing);
    }

    private bool TryGetActiveEntry(out DebugEntry entry)
    {
        entry = null!;

        var context = _httpContextAccessor.HttpContext;

        if (context == null ||
            !context.Items.TryGetValue("DebugProbeEntry", out var value) ||
            value is not DebugEntry activeEntry)
        {
            return false;
        }

        entry = activeEntry;
        return true;
    }

    private async Task<string> CaptureResponseBodyAsync(HttpResponseMessage? response, CancellationToken cancellationToken)
    {
        var content = response?.Content;
        if (content == null)
        {
            return string.Empty;
        }

        return await CaptureBodyAsync(content, replacement => response!.Content = replacement, cancellationToken);
    }

    private async Task<string> CaptureBodyAsync(
        HttpContent? content, Action<HttpContent> replaceContent, CancellationToken cancellationToken)
    {
        if (content == null || !HttpContentUtils.IsTextContent(content.Headers.ContentType?.MediaType) ||
            _options.MaxBodySizeBytes <= 0)
            return string.Empty;

        var limit = _options.MaxBodySizeBytes;
        var buffer = new byte[limit + 1];
        var totalRead = 0;
        var stream = await content.ReadAsStreamAsync(cancellationToken);
        var originalPosition = stream.CanSeek ? stream.Position : (long?)null;
        try
        {
            int bytesRead;
            while (totalRead < buffer.Length &&
                   (bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)) > 0)
                totalRead += bytesRead;
        }
        finally
        {
            // Restore consumed bytes even when capture fails or is cancelled midway through a read.
            if (originalPosition.HasValue)
                stream.Position = originalPosition.Value;
            else if (totalRead > 0)
                replaceContent(new PrefixReplayHttpContent(content, stream, buffer[..totalRead]));
        }

        var body = GetEncoding(content).GetString(buffer, 0, Math.Min(totalRead, limit));
        if (totalRead > limit)
            body += "[truncated]";

        return JsonUtils.Format(RedactionUtils.RedactJsonFields(body, _options));
    }

    private static Encoding GetEncoding(HttpContent content)
    {
        var charset = content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private sealed class PrefixReplayHttpContent : HttpContent
    {
        private readonly HttpContent _innerContent;

        private readonly Stream _remainingStream;

        private readonly byte[] _prefix;

        public PrefixReplayHttpContent(HttpContent innerContent, Stream remainingStream, byte[] prefix)
        {
            _innerContent = innerContent;
            _remainingStream = remainingStream;
            _prefix = prefix;

            foreach (var header in innerContent.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_prefix);
            await _remainingStream.CopyToAsync(stream);
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_innerContent.Headers.ContentLength is { } contentLength)
            {
                length = contentLength;
                return true;
            }

            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _remainingStream.Dispose();
                _innerContent.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
