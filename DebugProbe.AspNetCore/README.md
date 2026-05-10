# DebugProbe.AspNetCore 

<img src="https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/refs/heads/master/Assets/logo.png" width="120" />

**Debug HTTP traffic directly from inside your ASP.NET Core pipeline.**

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
![Requests](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/master/Assets/requests.png)

### Details
![Details](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/refs/heads/master/Assets/details_v1.3.0.png)

### Compare
![Compare](https://raw.githubusercontent.com/georgidhristov/DebugProbe.AspNetCore/refs/heads/master/Assets/compare_v1.3.0.png)

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

MIT
