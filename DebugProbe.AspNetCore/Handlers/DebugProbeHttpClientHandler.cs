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
    private static readonly HashSet<string> SensitiveHeaders =
    [
        "Authorization",
        "Cookie",
        "Set-Cookie"
    ];

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
    private async Task CaptureRequest(HttpRequestMessage request, HttpResponseMessage? response, Exception? exception,long durationMs)
    {
        var context = _httpContextAccessor.HttpContext;

        if (context == null)
        {
            return;
        }

        if (!context.Items.TryGetValue("DebugProbeEntry", out var value))
        {
            return;
        }

        if (value is not DebugEntry entry)
        {
            return;
        }

        var outgoing = new DebugOutgoingRequest
        {
            Method = request.Method.Method,

            Url = request.RequestUri?.ToString() ?? string.Empty,

            StatusCode = response != null ? (int)response.StatusCode : null,

            DurationMs = durationMs,

            Exception = exception?.ToString(),

            TimestampUtc = DateTime.UtcNow,

            IsSuccessStatusCode = response?.IsSuccessStatusCode ?? false,

            RequestHeaders = request.Headers.ToDictionary(x => x.Key, x => SensitiveHeaders.Contains(x.Key) ? "[REDACTED]" : string.Join(", ", x.Value)),

            ResponseHeaders = response != null ? response.Headers.ToDictionary(x => x.Key, x => SensitiveHeaders.Contains(x.Key) ? "[REDACTED]" : string.Join(", ", x.Value)) : []
        };

        if (request.Content != null)
        {
            var contentType = request.Content.Headers.ContentType?.MediaType;

            if (HttpContentUtils.IsTextContent(contentType))
            {
                var body = await request.Content.ReadAsStringAsync();

                outgoing.RequestBody = JsonUtils.Format(HttpContentUtils.Trim(body, _options.MaxBodyCaptureSizeKb * 1024));
            }
        }

        if (response?.Content != null)
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;

            if (HttpContentUtils.IsTextContent(contentType))
            {
                var body = await response.Content.ReadAsStringAsync();

                outgoing.ResponseBody = JsonUtils.Format(HttpContentUtils.Trim(body, _options.MaxBodyCaptureSizeKb * 1024));
            }
        }

        entry.OutgoingRequests.Add(outgoing);
    }
}