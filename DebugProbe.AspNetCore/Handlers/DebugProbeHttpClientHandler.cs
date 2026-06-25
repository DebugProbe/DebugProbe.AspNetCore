using System.Diagnostics;
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

            await CaptureRequest(request, response, null, started.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            await CaptureRequest(request, null, ex, started.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// Captures outgoing request details and stores them in the active DebugProbe entry.
    /// </summary>
    private async Task CaptureRequest(HttpRequestMessage request, HttpResponseMessage? response, Exception? exception, long durationMs)
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

        outgoing.RequestBody = await CaptureBodyAsync(request.Content);

        outgoing.ResponseBody = await CaptureBodyAsync(response?.Content);

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

    private async Task<string> CaptureBodyAsync(HttpContent? content)
    {
        if (content == null ||
            !HttpContentUtils.IsTextContent(content.Headers.ContentType?.MediaType))
        {
            return string.Empty;
        }

        if (_options.MaxBodyCaptureSizeBytes <= 0)
            return null;

        var limit = _options.MaxBodyCaptureSizeBytes;
        var buffer = new byte[limit + 1];
        var totalRead = 0;

        var stream = await content.ReadAsStreamAsync();

        int bytesRead;
        while (totalRead < buffer.Length &&
               (bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead))) > 0)
        {
            totalRead += bytesRead;
        }

        var truncated = totalRead > limit;
        var encoding = System.Text.Encoding.UTF8;
        var body = encoding.GetString(buffer, 0, Math.Min(totalRead, limit));

        if (truncated)
        {
            body += "[truncated]";
        }

        return JsonUtils.Format(RedactionUtils.RedactJsonFields(body, _options));
    }
}
