using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using Microsoft.Extensions.Hosting;

namespace DebugProbe.AspNetCore.Internal.Export;

internal sealed record ExportMessage(string Endpoint, object Payload);

// This is the server's schemaVersion=1 ingestion contract, not a reference to the server implementation.
internal sealed class TelemetryPayload(DebugProbeOptions options, IHostEnvironment host, DebugEnvironment environment)
{
    private readonly string instanceId = Guid.NewGuid().ToString("N");

    private object Application => new
    {
        options.Server!.ApplicationId,
        ApplicationName = options.Server.ApplicationName ?? host.ApplicationName,
        Environment = options.Server.Environment ?? host.EnvironmentName,
        InstanceId = instanceId,
        environment.MachineName, environment.AssemblyVersion, environment.Culture, environment.UiCulture, environment.TimeZone,
    };

    public ExportMessage Request(DebugEntry entry) => new("requests", new
    {
        SchemaVersion = 1, Application, TimestampUtc = entry.Timestamp.ToUniversalTime(), RequestId = entry.Id,
        entry.Method, entry.Path, entry.Query, Url = entry.RequestUrl, entry.StatusCode, entry.DurationMs,
        RequestHeaders = new Dictionary<string, string>(entry.RequestHeaders),
        ResponseHeaders = new Dictionary<string, string>(entry.ResponseHeaders),
        RequestBody = Body(entry.RequestBody, entry.RequestSize, options.CaptureRequestBody, ContentType(entry.RequestHeaders)),
        ResponseBody = Body(entry.ResponseBody, entry.ResponseSize, options.CaptureResponseBody, ContentType(entry.ResponseHeaders)),
        OutgoingRequests = entry.OutgoingRequests.ToArray().Select(call => new
        {
            call.Method, call.Url, call.StatusCode, call.DurationMs, call.TimestampUtc, call.IsSuccessStatusCode, call.Exception,
            RequestHeaders = new Dictionary<string, string>(call.RequestHeaders),
            ResponseHeaders = new Dictionary<string, string>(call.ResponseHeaders),
            RequestBody = Body(call.RequestBody, null, options.CaptureRequestBody, ContentType(call.RequestHeaders)),
            ResponseBody = Body(call.ResponseBody, null, options.CaptureResponseBody, ContentType(call.ResponseHeaders)),
        }).ToArray(),
    });

    public ExportMessage? Exception(DebugEntry entry)
    {
        if (entry.ExceptionType is null)
        {
            return null;
        }

        var firstLine = entry.ResponseBody.Split('\n', 2)[0].Trim();
        var colon = firstLine.IndexOf(':');
        
        return new("exceptions", new
        {
            SchemaVersion = 1, Application, TimestampUtc = entry.Timestamp.ToUniversalTime(), RequestId = entry.Id,
            entry.Method, entry.Path, Url = entry.RequestUrl, entry.StatusCode, entry.DurationMs,
            ExceptionType = entry.ExceptionType,
            Message = colon >= 0 && colon < firstLine.Length - 1 ? firstLine[(colon + 1)..].Trim() : "Exception details not captured.",
            StackTrace = string.IsNullOrEmpty(entry.ResponseBody) ? null : entry.ResponseBody,
            RequestHeaders = new Dictionary<string, string>(entry.RequestHeaders),
        });
    }

    public ExportMessage Heartbeat() => new("heartbeat", new
    {
        SchemaVersion = 1, Application, TimestampUtc = DateTimeOffset.UtcNow, Environment = environment,
    });

    private object Body(string? content, long? size, bool enabled, string? contentType)
    {
        var captured = enabled && options.MaxBodySizeKb > 0 && content is not null
            && content != BodyCaptureUtils.BinaryBodyMessage && content != BodyCaptureUtils.TruncatedRedactionMessage;
        
        return new
        {
            SizeBytes = size, Captured = captured,
            Truncated = content?.EndsWith("[truncated]", StringComparison.Ordinal) == true
                || content == BodyCaptureUtils.TruncatedRedactionMessage
                || (size.HasValue && size > options.MaxBodySizeBytes && enabled && options.MaxBodySizeKb > 0),
            ContentType = contentType, Content = captured ? content : null,
        };
    }

    private static string? ContentType(Dictionary<string, string> headers) =>
        headers.FirstOrDefault(x => x.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
}
