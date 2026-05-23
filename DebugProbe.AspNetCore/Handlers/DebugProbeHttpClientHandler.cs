using System.Diagnostics;
using DebugProbe.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Handlers;

public class DebugProbeHttpClientHandler : DelegatingHandler
{
    private static readonly HashSet<string> SensitiveHeaders =
    [
        "Authorization",
        "Cookie",
        "Set-Cookie"
    ];

    private readonly IHttpContextAccessor _httpContextAccessor;

    public DebugProbeHttpClientHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            CaptureRequest(request, response, null, started.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            CaptureRequest(
                request,
                null,
                ex,
                started.ElapsedMilliseconds);

            throw;
        }
    }

    private void CaptureRequest(HttpRequestMessage request, HttpResponseMessage? response, Exception? exception, long durationMs)
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

        entry.OutgoingRequests.Add(new DebugOutgoingRequest
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
        });
    }
}