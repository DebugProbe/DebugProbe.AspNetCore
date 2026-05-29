# DebugProbe Roadmap & Feature Planning

> A structured roadmap for evolving DebugProbe from a lightweight request inspector into a production-friendly debugging and observability toolkit for ASP.NET Core applications.

---

# Vision

DebugProbe aims to provide developers with a lightweight, embedded debugging experience for inspecting HTTP traffic inside ASP.NET Core applications — without requiring external infrastructure or heavyweight observability platforms.

The long-term direction is to keep DebugProbe:

* Lightweight
* Developer-first
* Easy to install
* Safe for internal environments
* Useful for distributed API debugging
* Extensible without becoming overly complex

---

# Current Capabilities

## Existing Features

### HTTP Traffic Inspection

* Incoming request capture
* Response capture
* Request body inspection
* Response body inspection
* Header inspection
* Timing and duration tracking

### Embedded Debug UI

* `/debug` dashboard
* Request details page
* Compare view between environments
* Embedded static assets

### Environment Comparison

* Remote trace comparison
* Environment metadata support

### Lightweight Architecture

* Minimal dependencies
* Simple setup
* In-memory bounded storage
* Easy ASP.NET Core integration

---

# Product Direction

The roadmap focuses on 5 major areas:

1. Security & Safety
2. Outgoing HTTP Visibility
3. Performance & Storage
4. Observability & Metrics
5. Developer Experience & UI

---

# Roadmap

# Phase 0 — Foundation & Safety

> Goal: Make DebugProbe safer and more production-friendly.

## Planned Features

### Response Header Capture

Capture and display response headers alongside requests.

#### Benefits

* Easier CORS debugging
* Cache inspection
* Authentication troubleshooting
* Better environment comparison

---

### Authentication for `/debug`

Protect debug endpoints with configurable authentication.

#### Proposed Options

* ASP.NET Core authorization policy
* Token-based access
* Environment-based enable/disable
* IP allowlist support

#### Benefits

* Safer staging deployments
* Prevent accidental exposure
* Production-readiness

---

### Safer Defaults

Improve out-of-box configuration.

#### Planned Defaults

* Ignore `/swagger`
* Ignore `/health`
* Ignore static assets
* Disable body capture in Production

---

### Configurable Capture Limits

Prevent excessive memory usage.

#### New Options

* `MaxBodyBytes`, `MaxStoredEntries`, `CaptureBodies`, `CaptureResponseHeaders`

#### Benefits

* Better stability
* Lower memory pressure
* Safer large payload handling

---

### Sensitive Data Redaction

Built-in redaction system.

#### Redaction Targets

* Authorization headers
* Cookies
* API keys
* Password fields
* Sensitive JSON fields

#### Planned Configuration

```csharp
options.RedactHeaders = ["Authorization", "Cookie"];
options.RedactJsonFields = ["password", "token"];
```

---

# Phase 1 — Outgoing HTTP Capture

> Goal: Add visibility into external API calls and downstream services.

## Planned Features

### HttpClient Instrumentation

Add automatic outgoing HTTP capture using `DelegatingHandler`.

#### Captured Data

* URL
* Method
* Status code
* Duration
* Request headers
* Response headers
* Request body
* Response body
* Exceptions

---

### Correlation Tracking

Link incoming requests with outgoing calls.

#### Features

* Trace ID propagation
* Request chain visibility
* End-to-end debugging

#### Example

```text
Incoming Request
 ├── External API Call #1
 ├── External API Call #2
 └── Database/API latency timeline
```

---

### Outgoing Requests Dashboard

New UI section for external calls.

#### Planned UI Features

* Recent outgoing requests
* Filter by host
* Error highlighting
* Latency sorting
* Group by service

---

### External Service Statistics

Aggregate metrics by destination host.

#### Example Metrics

* Average latency
* Failure rate
* Request count
* Slowest services

---

# Phase 2 — Storage & Persistence

> Goal: Move beyond temporary in-memory debugging.

## Planned Features

### Pluggable Storage System

Introduce sink/provider architecture.

#### Proposed Interface

```csharp
IDebugProbeSink
```

---

### Storage Providers

#### InMemorySink

Current implementation.

#### FileSink

* JSON-based storage
* Rolling files
* Lightweight persistence

#### SQLite Sink

* Searchable local history
* Structured querying
* Better retention support

#### Remote Collector Sink

* Centralized debugging
* Multi-instance aggregation
* Shared environments

---

### Export & Import

Allow traces to be shared and archived.

#### Supported Formats

* JSON
* CSV
* ZIP export bundles

---

### Retention Policies

Automatic cleanup rules.

#### Examples

* Maximum age
* Maximum size
* Maximum entries

---

# Phase 3 — Observability & Metrics

> Goal: Add lightweight operational insights.

## Planned Features

### Request Metrics

Aggregate request statistics.

#### Metrics

* Request count
* Error rate
* Median latency
* P95 latency
* Slowest endpoints

---

### External Dependency Metrics

Analyze downstream services.

#### Metrics

* Top external hosts
* Failed services
* Timeout rates
* Latency distribution

---

### Interactive Dashboard

Embedded lightweight charts.

#### Planned Visualizations

* Request timeline
* Latency graphs
* Error trends
* Throughput graphs

---

### Filtering & Search

Improve debugging workflow.

#### Filters

* Path
* Method
* Status code
* Host
* Trace ID
* Duration range

---

# Phase 4 — Developer Experience

> Goal: Make DebugProbe easier and faster to use.

## Planned Features

### Improved Compare View

Smarter request diffing.

#### Enhancements

* JSON field diff
* Highlighted changes
* Header comparison
* Timing comparison

---

### UI Modernization

Improve readability and usability.

#### Planned Improvements

* Better layout
* Responsive UI
* Dark mode
* Faster rendering
* Cleaner navigation

---

### Embedded Search

Global search across captured entries.

#### Search Targets

* Headers
* Bodies
* URLs
* Trace IDs

---

### Request Replay

Replay captured requests.

#### Use Cases

* Reproduce bugs
* Regression testing
* API debugging

---

### Plugin / Extension Hooks

Allow custom integrations.

#### Potential Extension Points

* Custom sinks
* Custom redactors
* Custom exporters
* Custom UI tabs

---

# Phase 5 — Advanced & Optional Features

> Goal: Expand into distributed debugging scenarios.

## Potential Features

### Distributed Trace Aggregation

Collect traces across multiple services.

---

### OpenTelemetry Integration

Optional interoperability with OTEL.

#### Possible Features

* Trace export
* Activity integration
* Span correlation

---

### Remote Debug Collector

Centralized debugging service.

#### Use Cases

* Multi-instance deployments
* Kubernetes environments
* Shared QA systems

---

### Team Collaboration Features

Potential long-term additions.

#### Examples

* Shared trace links
* Saved sessions
* Trace annotations

---

# Technical Principles

### Keep It Lightweight

DebugProbe should remain simple to install and remove.

### Avoid Heavy Dependencies

Prefer built-in ASP.NET Core features where possible.

### Production Safety First

Security and redaction should be first-class concerns.

### Extensible by Design

New features should support modular extension points.

---

# Suggested Priority Order

### High Priority (...---...)

* Authentication
* Redaction
* Body size limits
* Response headers
* Outgoing HTTP capture

### Medium Priority (-_^)

* Persistence
* Metrics dashboard
* Filtering/search
* Export/import

### Long-Term / Optional (•̀ᴗ•́)و

* Distributed tracing
* OpenTelemetry
* Remote collectors
* Replay tooling

---

# Milestone Recommendation

## Recommended Next Release Focus

### v1.Next

* Response header capture
* RequireAuth option
* Redaction support
* MaxBodyBytes
* Outgoing HTTP capture
* Outgoing requests UI tab

### v2

* Persistence providers
* Metrics dashboard
* Filtering/search
* Export/import

### v3

* Distributed tracing
* Remote collector
* OpenTelemetry integration

---

# Long-Term Goal

DebugProbe should become:

> The easiest way to inspect and compare HTTP behavior inside ASP.NET Core applications.

While still remaining:

* lightweight
* embeddable
* developer-friendly
* infrastructure-optional
* safe for internal environments
