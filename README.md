# DebugProbe.AspNetCore

Debug HTTP traffic directly inside your ASP.NET Core pipeline.

[DebugProbe Website](https://debugprobe.dev)

---

## Why DebugProbe?

- Inspect requests and responses in real time
- No proxies or external tools
- Built-in request tracing UI
- Compare responses across environments
- Minimal setup for ASP.NET Core applications

---

## Install

```bash
dotnet add package DebugProbe.AspNetCore
```

---

## Quick Start

```csharp
builder.Services.AddDebugProbe();

app.UseDebugProbe();
```

Open:

```txt
http://localhost:{port}/debug
```

---

## Optional Configuration

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.MaxEntries = 10;

    options.MaxBodyCaptureSizeKb = 256;

    options.AllowLocalCompareTargets = true;

    options.IgnorePaths =
    [
        "/api/auth/login",
        "/api/auth/refresh"
    ];
});

app.UseDebugProbe();
```

---

## Features

- Request and response inspection
- Headers, query params, and body capture
- Response comparison across environments
- JSON formatting
- Configurable body capture limits
- Ignore noisy endpoints
- Sensitive header masking

---

## Security Defaults

Sensitive headers are automatically masked:

- Authorization
- Cookie
- Set-Cookie

Localhost compare targets are blocked by default.

---

## Production Usage

DebugProbe is intended primarily for development environments.

If used in production:

- add authentication
- restrict access
- filter sensitive data

---

## Documentation & Demo

Visit the website for:
- latest screenshots
- demos
- guides
- updates

https://debugprobe.dev

---

## License

Apache License 2.0