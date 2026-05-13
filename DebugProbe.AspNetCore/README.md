﻿# DebugProbe.AspNetCore 

**Debug HTTP traffic directly from inside your ASP.NET Core pipeline.**

[![DebugProbe](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/debugprobe_icon_white_rounded_180px.png)](https://debugprobe.dev)

Live Demo: https://debugprobe.dev

## Why DebugProbe?

- Debug real requests from inside your app
- No proxy setup or traffic interception
- See exactly what your API sends and receives
- Compare environments in seconds


## Features

- Capture HTTP requests and responses
- Inspect headers, query params, and body
- Built-in request tracing UI
- Compare responses across environments
- JSON pretty formatting
- Ignore noisy endpoints with `IgnorePaths`
- Zero external proxies or setup


## Screenshots

### Requests
![Requests](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/requests.png)

### Details
![Details](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/details_v1.4.0-preview.1.png)

### Compare
![Compare](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/compare_v1.4.0-preview.1.png)

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

![DebugProbe Short Demo](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/main/Assets/debugprobe_demo_live_debugging.gif)


## Compare Responses

Use the UI to compare responses across environments:

- Enter **Base URL**
- Enter **Trace ID**
- Instantly see differences

## ⚠️ Production Usage

This tool is intended for development.

If used in production:

- Add authentication
- Restrict access
- Filter sensitive data


## License

Apache License 2.0
