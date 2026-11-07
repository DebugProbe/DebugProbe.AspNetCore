# DebugProbe.AspNetCore

DebugProbe.AspNetCore is a lightweight ASP.NET Core debugging tool for inspecting HTTP traffic directly inside your application.

It captures incoming requests and outgoing HTTP calls, provides an embedded dashboard, and compares captured requests across environments. Starting with 2.1.0, it can also send telemetry to DebugProbe Server.

## DebugProbe products

| Product | Purpose | Status |
| --- | --- | --- |
| **DebugProbe.AspNetCore** | Capture and inspect HTTP traffic inside an ASP.NET Core application; optionally export telemetry | The package in this repository; server export implemented for 2.1.0 |
| **DebugProbe Server** | Centralized telemetry and team access, hosted by the customer on IIS or Docker | In development; PostgreSQL, accounts, access grants, ingestion and local Docker setup are implemented |
| **DebugProbe Cloud** | A hosted SaaS service operated by DebugProbe | Planned after DebugProbe Server is ready |

The package works independently with its local dashboard. Server is an optional destination; it stores telemetry and manages access separately from the instrumented application. See the [product roadmap](https://github.com/DebugProbe/DebugProbe.AspNetCore/blob/main/Roadmap.md) for the development order and remaining work.

## Links

- Website: [debugprobe.dev](https://debugprobe.dev)
- Documentation: [debugprobe.dev/docs](https://debugprobe.dev/docs)
- Roadmap: [Roadmap.md](https://github.com/DebugProbe/DebugProbe.AspNetCore/blob/main/Roadmap.md)
- NuGet: [DebugProbe.AspNetCore](https://www.nuget.org/packages/DebugProbe.AspNetCore)

## Version Compatibility

| DebugProbe.AspNetCore | Target framework | Application requirement |
| --- | --- | --- |
| 2.x, including 2.1.0 | `net10.0` | .NET 10 / ASP.NET Core 10 |
| 1.x releases targeting net8.0 (for example, 1.6.10) | `net8.0` | Use a compatible 1.x release for applications staying on .NET 8 or 9 |

Versions 2.0.0 and 2.1.0 require .NET 10 and ASP.NET Core 10. Building this repository requires the .NET 10 SDK.
Applications staying on .NET 8 or .NET 9 should use a compatible 1.x release, such as 1.6.10:

```bash
dotnet add package DebugProbe.AspNetCore --version 1.6.10
```

Compatibility does not extend Microsoft's support lifecycle for the application's .NET runtime.
Version 2.0.0 also contains breaking configuration and API changes described below.

## Install

```bash
dotnet add package DebugProbe.AspNetCore
```

## Quick Start

```csharp
using DebugProbe.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDebugProbe();

var app = builder.Build();
app.UseDebugProbe();
app.MapGet("/hello", () => "Hello!");
app.Run();
```

Start your application and open:

```txt
http://localhost:{port}/debug
```

In Production, DebugProbe captures traces but does not register dashboard, JSON, environment or compare endpoints unless explicitly enabled. Local capture data is kept in memory and is lost when the application stops.
DebugProbe does not require authentication by default.

## Send telemetry to DebugProbe Server (2.1+)

Export is optional. Without `Server` configuration, DebugProbe keeps working locally as before.

In the server's **Settings → API keys**, create an ingestion key for your application's ID and environment. Configure the same values in the application:

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.Server = new()
    {
        Url = "http://localhost:18080",
        ApiKey = "<ingestion-key-from-server>",
        ApplicationId = "my-api",
        ApplicationName = "My API", // optional; defaults to the host application's name
        Environment = "Development"
    };
});
```

Keep `app.UseDebugProbe()` in the pipeline as usual. The server's base URL may include a path prefix. Application ID and environment are case-sensitive and must match the key. A null environment uses `IHostEnvironment.EnvironmentName`; an empty string means the unnamed environment. Use HTTPS when the server is not local. TLS certificates are validated normally; redirects are not followed.

To bind from `appsettings.Development.json` instead, use this as an alternative to the configuration callback:

```csharp
using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Options;

builder.Services.AddDebugProbe();
builder.Services.Configure<DebugProbeOptions>(builder.Configuration.GetSection("DebugProbe"));
```

```json
{
  "DebugProbe": {
    "Server": {
      "Url": "http://localhost:18080",
      "ApiKey": "<ingestion-key-from-server>",
      "ApplicationId": "my-api",
      "Environment": "Development"
    }
  }
}
```

One background worker posts requests (including captured outgoing calls), unhandled exceptions and application heartbeats to the server's schema-version-1 ingestion endpoints. It uses `X-DebugProbe-Key`; browser login credentials are not used. Requests and exception events share the captured request ID. Heartbeats are sent at startup and then every minute, with a stable instance ID for that application process. Exception events cover exceptions observed by the capture middleware; exceptions handled entirely inside application code remain ordinary captured requests.

Export uses already captured/redacted bodies, headers and URLs, respecting the existing capture switches and body limits. The internal export client is excluded from outgoing-call capture, so it cannot recursively export its own traffic. Local dashboard storage and export queues are independent; `MaxRequests` does not limit the number of events sent over time.

Delivery is **best effort**: requests never wait for network delivery. The queue holds up to 256 events, each HTTP send times out after five seconds, and failures or full queues drop events. There are no retries, disk spool or extra infrastructure. Warnings are limited to one per 30 seconds and do not include keys, payloads or response bodies. Graceful shutdown allows up to five seconds for queued events before cancellation; abrupt termination can lose queued events.

Export settings:

| Setting | Default / behavior |
| --- | --- |
| `Server` | `null`: no remote export |
| `Server.Url` | Required server base URL; the package appends `/api/ingestion/` |
| `Server.ApiKey` | Required ingestion key issued by that server |
| `Server.ApplicationId` | Required application ID matching the key |
| `Server.ApplicationName` | Host application's name |
| `Server.Environment` | Host environment when null; empty string is the unnamed environment |
| `Server.QueueCapacity` | 256 queued events |
| `Server.Timeout` | Five seconds per HTTP send |
| `Server.HeartbeatInterval` | One minute |

Invalid export configuration fails at startup. `Enabled = false` disables export as well as local capture. Server export does not require the local UI to be enabled in Production. JSON configuration uses TimeSpan strings such as `"00:00:05"` for timeout values.

**DebugProbe.SampleApi** is included in the source repository, not in the NuGet package. Its development configuration targets a local Docker server at port 18080, using application ID `debugprobe-sample`, environment `Development` and a development ingestion key. That key is specific to the development server where it was issued; another installation needs its own key. Start the server and sample, call `/weatherforecast` or `/Demo/GetUser/500`, then refresh DebugProbe Server.

## Optional Configuration

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.MaxRequests = 10;

    options.MaxBodySizeKb = 256;

    options.AllowLocalCompareTargets = true;

    options.AllowUiInProduction = false;

    options.CaptureOutgoingRequests = true;

    options.ExcludedPaths =
    [
        ..options.ExcludedPaths,
        "/api/auth/login",
        "/api/auth/refresh"
    ];

    options.RedactedHeaders =
    [
        ..options.RedactedHeaders,
        "X-Api-Key",
        "X-Auth-Token"
    ];

    options.RedactedQueryParameters =
    [
        "api_key",
        "access_token"
    ];

    options.RedactedJsonFields =
    [
        "password",
        "refreshToken"
    ];
});

app.UseDebugProbe();
```

Register configuration through `AddDebugProbe` or the standard options API; `UseDebugProbe()` enables the middleware and endpoints without accepting a configuration callback.

- `Enabled = false` disables capture and all DebugProbe endpoints.
- `MaxBodySizeKb = 0` disables all body capture. Headers and request metadata are still captured.
- `MaxRequests` defaults to 20 locally retained requests; `MaxBodySizeKb` defaults to 32 per body.
- `CaptureRequestBody` and `CaptureResponseBody` independently control incoming and outgoing bodies (both default to `true`).
- `SlowRequestThreshold = TimeSpan.FromSeconds(1)` controls slow badges; `TimeSpan.Zero` disables them.
- `ExcludedPaths` replaces the defaults. Use `[..options.ExcludedPaths, "/other"]` to extend them, or `[]` to capture all application paths.
- `RoutePrefix` defaults to `"/debug"` and applies to the dashboard, assets, APIs, and compare page (`/debug/compare`).

Standard .NET options configuration is also supported:

```csharp
using DebugProbe.AspNetCore.Options;

builder.Services.AddDebugProbe();
builder.Services.Configure<DebugProbeOptions>(builder.Configuration.GetSection("DebugProbe"));
```

Body capture uses the same rules for incoming and outgoing requests and responses:

- The size limit applies to original body bytes; oversized text keeps a prefix followed by `[truncated]`.
- Text uses the declared charset, with UTF-8 as the fallback; truncation does not leave a partial encoded character.
- Binary content is represented by `[Body not captured: non-text content]`; empty or disabled bodies are empty strings.
- If JSON field redaction is configured, truncated content is omitted because incomplete JSON cannot be reliably redacted.
- Captured JSON is redacted before storage and formatted for display. Application payloads remain intact.

Options are validated at startup. Configuration is fixed for the application's lifetime.

## Migrating from 1.x to 2.0

- Upgrade the application to .NET 10 before installing version 2.0.0.
- Configure DebugProbe in `builder.Services.AddDebugProbe(...)`, before `builder.Build()`; replace `app.UseDebugProbe(options => ...)` with `app.UseDebugProbe()`.

| Previous option | Replacement |
| --- | --- |
| `MaxEntries` | `MaxRequests` |
| `MaxBodyCaptureSizeKb` | `MaxBodySizeKb` |
| `IgnorePaths` | `ExcludedPaths` |
| `CaptureOutgoingHttpClientRequests` | `CaptureOutgoingRequests` |
| `SlowRequestThresholdMs = 1000` | `SlowRequestThreshold = TimeSpan.FromSeconds(1)` |

`TrendLookbackMinutes`, `AutoEnvironmentDiff`, and `RedactionText` have been removed. Sensitive values use the fixed `[REDACTED]` marker; trace comparison remains available manually.

`ExcludedPaths` now contains editable defaults. Assigning an array replaces them; use `[..options.ExcludedPaths, "/other"]` to extend them. DebugProbe endpoints remain excluded automatically.

Additional API and behavior changes:

- `DebugEntryStore.Instance` has been removed; resolve the store through dependency injection when needed.
- `OutgoingRequests` is a concurrent queue: use `Enqueue(...)` instead of `Add(...)`.
- `ExceptionGroups` returns an `IReadOnlyDictionary` snapshot instead of a mutable concurrent dictionary.
- The compare page moved from `/compare` to `/debug/compare` (or `{RoutePrefix}/compare`); update bookmarks and links. Remote trace comparison currently uses the same route prefix on both applications.
- Oversized text now keeps a prefix with `[truncated]` instead of `[Body too large]`. When JSON field redaction is configured, truncated content is omitted.
- Captured outgoing JSON is stored without pretty-printing and formatted for display, matching incoming capture.

For endpoint authorization, see **Security Defaults** below.

## Features

- Request inspection
- Response inspection
- Headers, query string, and body capture
- Error visibility
- Local debugging dashboard
- Trace comparison across runs or environments
- JSON formatting for captured payloads
- Configurable body capture limits
- Ignored path configuration for noisy or sensitive endpoints
- Configurable redaction for sensitive headers, query parameters, and JSON fields
- Optional ASP.NET Core authorization policy protection for DebugProbe endpoints
- Optional outgoing `HttpClient` request tracing
- Optional authenticated export of requests, unhandled exceptions and heartbeats to DebugProbe Server

Outgoing capture applies to clients created through `IHttpClientFactory` while an incoming request is being captured. Calls made by a standalone `new HttpClient()` or outside an active captured request are not automatically included.

## Trace Compare

DebugProbe can compare a local trace with a trace captured by another DebugProbe-enabled application.

Typical workflow:

1. Run both applications with DebugProbe enabled.
2. Open the local dashboard at `/debug`.
3. Open the trace you want to compare.
4. Use the compare action and provide the remote application's base URL and trace ID.

Compare is useful when checking differences between local and remote environments, repeated runs, or two versions of the same API flow.

Comparison includes JSON-aware request/response body differences, status codes and displayed request/environment metadata. Dynamic IDs, timestamps and tokens are not automatically normalized or ignored. Both applications currently need the same DebugProbe route prefix, and the remote application's JSON/environment endpoints must be reachable. `AllowLocalCompareTargets` permits local/private-network targets by default only in Development; it is separate from the server export URL.

## Security Defaults

DebugProbe UI endpoints are disabled by default in Production. Capture and trace storage continue to run, but the dashboard, trace viewer, compare UI, UI assets, and UI clear action are not registered unless explicitly enabled:

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.AllowUiInProduction = true;
});
```

DebugProbe does not require authentication by default. If you expose DebugProbe outside local development, configure an ASP.NET Core authorization policy and require it for DebugProbe endpoints:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DebugProbePolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin");
    });
});

builder.Services.AddDebugProbe(options =>
{
    options.AuthorizationPolicy = "DebugProbePolicy";
});

app.UseAuthentication();
app.UseAuthorization();
app.UseDebugProbe();
```

DebugProbe masks common sensitive headers automatically:

- `Authorization`
- `Cookie`
- `Set-Cookie`

You can also configure application-specific values to redact before traces are stored:

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.RedactedHeaders =
    [
        ..options.RedactedHeaders,
        "X-Api-Key",
        "Client-Secret"
    ];

    options.RedactedQueryParameters = ["token", "api_key", "access_token"];

    options.RedactedJsonFields = ["password", "secret", "refreshToken"];
});
```

Query-parameter and JSON-field redaction lists are empty by default. Redaction matches the configured names; it is not a general scrubber for arbitrary text, exception messages or URL paths. Choose capture and redaction settings before enabling remote export, since exported telemetry uses the captured data.

## Intended Usage

DebugProbe is designed primarily for local development and controlled development environments.

If you use it outside local development, protect the dashboard with authentication, restrict network access, and avoid capturing sensitive endpoints or payloads.

## Documentation

For full setup details, screenshots, dashboard behavior, configuration options, and live examples, see the documentation:

[https://debugprobe.dev/docs](https://debugprobe.dev/docs)

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](https://github.com/DebugProbe/DebugProbe.AspNetCore?tab=contributing-ov-file) before opening an issue or pull request.

## License

DebugProbe.AspNetCore is licensed under the [Apache License 2.0](https://github.com/DebugProbe/DebugProbe.AspNetCore/blob/main/LICENSE).
