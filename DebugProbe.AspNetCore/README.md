# DebugProbe.AspNetCore

DebugProbe.AspNetCore is a lightweight ASP.NET Core debugging tool for inspecting HTTP traffic directly inside your application.

It captures request and response data, exposes a local dashboard, and helps compare traces across environments so you can spot differences between local, staging, and other API runs.

## Links

- Website: [debugprobe.dev](https://debugprobe.dev)
- Documentation: [debugprobe.dev/docs](https://debugprobe.dev/docs)
- Roadmap: [Roadmap.md](https://github.com/DebugProbe/DebugProbe.AspNetCore/blob/main/Roadmap.md)
- Live demo: [demo.debugprobe.dev/debug](https://demo.debugprobe.dev/debug)
- Demo API: [demo.debugprobe.dev/swagger](https://demo.debugprobe.dev/swagger)
- NuGet: [DebugProbe.AspNetCore](https://www.nuget.org/packages/DebugProbe.AspNetCore)

## Install

```bash
dotnet add package DebugProbe.AspNetCore
```

## Quick Start

```csharp
builder.Services.AddDebugProbe();

app.UseDebugProbe();
```

Start your application and open:

```txt
http://localhost:{port}/debug
```

In Production, DebugProbe captures traces but does not register UI endpoints unless explicitly enabled.
DebugProbe does not require authentication by default.

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

Configure options in `AddDebugProbe`; `UseDebugProbe()` only enables the middleware and endpoints.

- `Enabled = false` disables capture and all DebugProbe endpoints.
- `MaxBodySizeKb = 0` disables all body capture. Headers and request metadata are still captured.
- `CaptureRequestBody` and `CaptureResponseBody` independently control incoming and outgoing bodies (both default to `true`).
- `SlowRequestThreshold = TimeSpan.FromSeconds(1)` controls slow badges; `TimeSpan.Zero` disables them.
- `ExcludedPaths` replaces the defaults. Use `[..options.ExcludedPaths, "/other"]` to extend them, or `[]` to capture all application paths.
- `RoutePrefix` defaults to `"/debug"` and applies to the dashboard, assets, APIs, and compare page (`/debug/compare`).

Standard .NET options configuration is also supported:

```csharp
builder.Services.AddDebugProbe();
builder.Services.Configure<DebugProbeOptions>(builder.Configuration.GetSection("DebugProbe"));
```

Options are validated at startup. Configuration is fixed for the application's lifetime.

This refactor renames `MaxEntries` to `MaxRequests`, `MaxBodyCaptureSizeKb` to `MaxBodySizeKb`,
`IgnorePaths` to `ExcludedPaths`, and `CaptureOutgoingHttpClientRequests` to `CaptureOutgoingRequests`.
`SlowRequestThresholdMs` is replaced by the `TimeSpan`-valued `SlowRequestThreshold`.
`TrendLookbackMinutes`, `AutoEnvironmentDiff`, `RedactionText`, and the configuration overload of `UseDebugProbe` have been removed.
Sensitive values use the fixed `[REDACTED]` marker. Trace comparison remains available manually.

To protect DebugProbe endpoints, configure an ASP.NET Core authorization policy and require it when DebugProbe is registered:

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

## Trace Compare

DebugProbe can compare a local trace with a trace captured by another DebugProbe-enabled application.

Typical workflow:

1. Run both applications with DebugProbe enabled.
2. Open the local dashboard at `/debug`.
3. Open the trace you want to compare.
4. Use the compare action and provide the remote application's base URL and trace ID.

Compare is useful when checking differences between local and remote environments, repeated runs, or two versions of the same API flow.

Dynamic values such as IDs, timestamps, tokens, and selected headers are normalized so the compare view focuses on meaningful request and response differences.

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
