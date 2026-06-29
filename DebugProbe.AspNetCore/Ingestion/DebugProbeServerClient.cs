using System.Net.Http.Json;
using System.Reflection;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;

namespace DebugProbe.AspNetCore.Ingestion;

public sealed class DebugProbeServerClient
{
    private const string BodyTooLargeMessage = "[Body too large]";
    private const string BinaryBodyMessage = "[Body not captured: non-text content]";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private readonly DebugProbeOptions _options;

    public DebugProbeServerClient(DebugProbeOptions options)
    {
        _options = options;
    }

    public async Task SendRequestAsync(DebugEntry entry, DebugEnvironment environment)
    {
        if (!TryGetEndpoint(out var endpoint))
        {
            return;
        }

        try
        {
            await Http.PostAsJsonAsync(endpoint, MapRequest(entry, environment));
        }
        catch
        {
            // Central ingestion is optional and must never break the local application.
        }
    }

    private bool TryGetEndpoint(out Uri endpoint)
    {
        endpoint = default!;

        if (string.IsNullOrWhiteSpace(_options.ServerUrl) ||
            !Uri.TryCreate(_options.ServerUrl, UriKind.Absolute, out var serverUri))
        {
            return false;
        }

        endpoint = new Uri(EnsureTrailingSlash(serverUri), "api/ingestion/requests");
        return true;
    }

    private RequestData MapRequest(DebugEntry entry, DebugEnvironment environment)
    {
        return new RequestData
        {
            Application = MapApplication(environment),
            TimestampUtc = entry.RequestTimeUtc.ToUniversalTime(),
            RequestId = entry.Id,
            Method = entry.Method,
            Path = entry.Path,
            Query = entry.Query,
            Url = entry.RequestUrl,
            DurationMs = entry.DurationMs,
            StatusCode = entry.StatusCode,
            RequestBody = MapBody(entry.RequestBody, entry.RequestSize),
            ResponseBody = MapBody(entry.ResponseBody, entry.ResponseSize),
            RequestHeaders = entry.RequestHeaders,
            ResponseHeaders = entry.ResponseHeaders,
            OutgoingRequests = entry.OutgoingRequests.Select(MapOutgoingRequest).ToList()
        };
    }

    private ApplicationData MapApplication(DebugEnvironment environment)
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "Application";

        return new ApplicationData
        {
            ApplicationId = string.IsNullOrWhiteSpace(_options.ApplicationId) ? assemblyName : _options.ApplicationId,
            ApplicationName = string.IsNullOrWhiteSpace(_options.ApplicationName) ? assemblyName : _options.ApplicationName,
            Environment = environment.Environment,
            InstanceId = _options.InstanceId,
            MachineName = environment.MachineName,
            AssemblyVersion = environment.AssemblyVersion,
            Culture = environment.Culture,
            UiCulture = environment.UiCulture,
            TimeZone = environment.TimeZone
        };
    }

    private static OutgoingRequestData MapOutgoingRequest(DebugOutgoingRequest outgoing)
    {
        return new OutgoingRequestData
        {
            Method = outgoing.Method,
            Url = outgoing.Url,
            StatusCode = outgoing.StatusCode,
            DurationMs = outgoing.DurationMs,
            TimestampUtc = new DateTimeOffset(DateTime.SpecifyKind(outgoing.TimestampUtc, DateTimeKind.Utc)),
            IsSuccessStatusCode = outgoing.IsSuccessStatusCode,
            RequestBody = MapBody(outgoing.RequestBody, null),
            ResponseBody = MapBody(outgoing.ResponseBody, null),
            Exception = outgoing.Exception,
            RequestHeaders = outgoing.RequestHeaders,
            ResponseHeaders = outgoing.ResponseHeaders
        };
    }

    private static BodyData? MapBody(string? content, long? sizeBytes)
    {
        if (content is null)
        {
            return null;
        }

        return new BodyData
        {
            SizeBytes = sizeBytes,
            Captured = content.Length > 0 && content != BodyTooLargeMessage && content != BinaryBodyMessage,
            Truncated = content == BodyTooLargeMessage || content.EndsWith("[truncated]", StringComparison.Ordinal),
            Content = content
        };
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(value + "/");
    }

    private sealed class RequestData
    {
        public int SchemaVersion { get; set; } = 1;
        public ApplicationData Application { get; set; } = new();
        public DateTimeOffset TimestampUtc { get; set; }
        public string? RequestId { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Query { get; set; }
        public string? Url { get; set; }
        public long DurationMs { get; set; }
        public int StatusCode { get; set; }
        public BodyData? RequestBody { get; set; }
        public BodyData? ResponseBody { get; set; }
        public Dictionary<string, string> RequestHeaders { get; set; } = [];
        public Dictionary<string, string> ResponseHeaders { get; set; } = [];
        public List<OutgoingRequestData> OutgoingRequests { get; set; } = [];
    }

    private sealed class ApplicationData
    {
        public string ApplicationId { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string? Environment { get; set; }
        public string? InstanceId { get; set; }
        public string? MachineName { get; set; }
        public string? AssemblyVersion { get; set; }
        public string? Culture { get; set; }
        public string? UiCulture { get; set; }
        public string? TimeZone { get; set; }
    }

    private sealed class BodyData
    {
        public long? SizeBytes { get; set; }
        public bool Captured { get; set; }
        public bool Truncated { get; set; }
        public string? Content { get; set; }
    }

    private sealed class OutgoingRequestData
    {
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public long DurationMs { get; set; }
        public DateTimeOffset? TimestampUtc { get; set; }
        public bool? IsSuccessStatusCode { get; set; }
        public BodyData? RequestBody { get; set; }
        public BodyData? ResponseBody { get; set; }
        public string? Exception { get; set; }
        public Dictionary<string, string> RequestHeaders { get; set; } = [];
        public Dictionary<string, string> ResponseHeaders { get; set; } = [];
    }
}