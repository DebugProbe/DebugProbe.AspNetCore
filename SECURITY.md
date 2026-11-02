# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in DebugProbe.AspNetCore, please report it privately.

Please do not open public GitHub issues for security-related reports.

Contact via [Email](mailto:georgi.dimitrov.hristov@gmail.com)

You can also use [GitHub Private Vulnerability Reporting](https://github.com/DebugProbe/DebugProbe.AspNetCore/security/advisories/new).

Include:

- Description of the issue
- Steps to reproduce
- Potential impact
- Suggested fix (if available)

You will receive a response as soon as possible.

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest  |    YES    |
| Older   |    NO     |

## Security Notes

DebugProbe.AspNetCore is a development and debugging tool intended primarily for local and non-production environments.

By default:

- Data is stored in-memory only
- Data is not persisted externally
- Stored entries are cleared when the application stops
- Only a limited number of requests are retained
- DebugProbe endpoints do not require authentication unless an authorization policy is configured

When exposing DebugProbe outside local development, prefer protecting the endpoints with an ASP.NET Core authorization policy:

```csharp
// Register the policy with AddAuthorization before building the application.
builder.Services.AddDebugProbe(options =>
{
    options.AuthorizationPolicy = "DebugProbePolicy";
});

// After builder.Build(), enable the configured authentication/authorization middleware.
app.UseAuthentication();
app.UseAuthorization();
app.UseDebugProbe();
```

Avoid exposing DebugProbe endpoints publicly or using the package in production environments without proper security review and access restrictions.

Users are responsible for filtering sensitive headers, tokens, cookies, and personal data where necessary.
