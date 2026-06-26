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

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            await CaptureRequest(request, response, null, started.ElapsedMilliseconds, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            await CaptureRequest(request, null, ex, started.ElapsedMilliseconds, cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// Captures outgoing request details and stores them in the active DebugProbe entry.
    /// </summary>
    private async Task CaptureRequest(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception? exception,
        long durationMs,
        CancellationToken cancellationToken)
    {
        if (!TryGetActiveEntry(out var entry))
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

        outgoing.RequestBody = (await CaptureBodyAsync(request.Content, cancellationToken)).Body;

        outgoing.ResponseBody = await CaptureResponseBodyAsync(response, cancellationToken);

        entry.OutgoingRequests.Add(outgoing);
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

        var result = await CaptureBodyAsync(content, cancellationToken);

        if (result.BytesRead.Length > 0)
        {
            response!.Content = new PrefixReplayHttpContent(content, result.Stream, result.BytesRead);
        }

        return result.Body;
    }

    private async Task<BodyCaptureResult> CaptureBodyAsync(HttpContent? content, CancellationToken cancellationToken)
    {
        if (content == null ||
            !HttpContentUtils.IsTextContent(content.Headers.ContentType?.MediaType))
        {
            return string.Empty;
        }

        if (_options.MaxBodyCaptureSizeBytes <= 0)
            return string.Empty;

        var limit = _options.MaxBodyCaptureSizeBytes;
        var buffer = new byte[limit + 1];
        var totalRead = 0;

        var stream = await content.ReadAsStreamAsync(cancellationToken);

        int bytesRead;
        while (totalRead < buffer.Length &&
               (bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)) > 0)
        {
            totalRead += bytesRead;
        }

        var truncated = totalRead > limit;
        var encoding = GetEncoding(content);
        var body = encoding.GetString(buffer, 0, Math.Min(totalRead, limit));

        if (truncated)
        {
            body += "[truncated]";
        }

        return new BodyCaptureResult(
            JsonUtils.Format(RedactionUtils.RedactJsonFields(body, _options)),
            stream,
            buffer[..totalRead]);
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

    private readonly record struct BodyCaptureResult(string Body, Stream Stream, byte[] BytesRead)
    {
        public static implicit operator BodyCaptureResult(string body) => new(body, Stream.Null, []);
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
