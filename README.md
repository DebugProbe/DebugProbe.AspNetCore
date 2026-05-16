# DebugProbe.AspNetCore 

**Debug HTTP traffic directly from inside your ASP.NET Core pipeline.**

[![DebugProbe](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/Logos/debugprobe_icon_white_rounded_180px.png)](https://debugprobe.dev)

Live Demo: https://debugprobe.dev

## Why Use DebugProbe?

- Debug HTTP traffic directly inside your ASP.NET Core pipeline
- No proxies, browser extensions, or external tools
- Inspect requests and responses in real time
- Compare API responses across environments instantly
- Built for fast backend debugging with minimal setup


## Features

- Capture HTTP requests and responses
- Inspect headers, query params, and body
- Built-in request tracing UI
- Compare responses across environments
- JSON pretty formatting
- Ignore noisy endpoints with `IgnorePaths`
- Configurable body capture size limits
- Safe compare mode with localhost protection
- Automatic masking of sensitive headers
- Zero external proxies or setup


## Screenshots

### Requests
![Requests](https://raw.githubusercontent.com/DebugProbe/DebugProbe.AspNetCore/main/Assets/Screenshots/debugprobe_index_page_requests.png)

### Details
![Details_overview](https://raw.githubusercontent.com/DebugProbe/DebugProbe.AspNetCore/main/Assets/Screenshots/debugprobe-details-overview.png)

![Details_request_response](https://raw.githubusercontent.com/DebugProbe/DebugProbe.AspNetCore/main/Assets/Screenshots/debugprobe-details-request-response.png)

### Compare
![Compare](https://raw.githubusercontent.com/DebugProbe/DebugProbe.AspNetCore/main/Assets/Screenshots/debugprobe_compare_page.png)

---

## Install

```bash
dotnet add package DebugProbe.AspNetCore
```

## Quick Start

```csharp
builder.Services.AddDebugProbe();

app.UseDebugProbe();
```

## Customize DebugProbe

```csharp
builder.Services.AddDebugProbe(options =>
{
    options.MaxEntries = 10;

    options.MaxBodyCaptureSizeKb = 256;

    options.AllowLocalCompareTargets = false;

    options.IgnorePaths =
    [
        "/health",
        "/swagger",
        "/Demo/GetUsers"
    ];
});

app.UseDebugProbe();
```

## Open The Debug UI

Run your application, then open:

http://localhost:{port}/debug

![DebugProbe Short Demo](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/Demos/debugprobe_demo_live_debugging.gif)

## Compare Responses

Use the UI to compare responses across environments:

- Enter **Base URL**
- Enter **Trace ID**
- Instantly see differences

## Security Defaults

DebugProbe automatically masks sensitive headers:

- Authorization
- Cookie
- Set-Cookie

Localhost compare targets are blocked by default for safer environment comparisons.

You can enable them manually:

```csharp
options.AllowLocalCompareTargets = true;
```

## ⚠️ Production Usage

This tool is intended for development.

If used in production:

- Add authentication
- Restrict access
- Filter sensitive data

## License

Apache License 2.0