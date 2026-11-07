# DebugProbe roadmap

DebugProbe helps developers inspect HTTP traffic, investigate exceptions and compare application behavior across environments.

The development order is **DebugProbe.AspNetCore → DebugProbe Server → DebugProbe Cloud**. The package remains useful on its own. The immediate product priority is completing the customer-hosted Server before building the hosted SaaS service.

This roadmap describes current capabilities, agreed next steps and later candidates. It is not a release schedule; future features have no assigned package versions or delivery dates.

## Product responsibilities

| Product | Responsibility | Current stage |
| --- | --- | --- |
| **DebugProbe.AspNetCore** | Capture HTTP data inside an application, provide local inspection, optionally export telemetry | Implemented; optional Server export is the 2.1.0 feature |
| **DebugProbe Server** | Store telemetry from multiple applications and instances, provide a central UI, manage users and permissions | In development; core ingestion, persistence, access management and local Docker setup are implemented |
| **DebugProbe Cloud** | Operate DebugProbe as a hosted subscription service with customer isolation and managed infrastructure | Planned after Server is ready |

Customers host Server on their infrastructure, with IIS or Docker as deployment targets. DebugProbe will host and operate Cloud. Both destinations should use a compatible, versioned ingestion contract so applications can keep using the same package and configure their destination.

## Implemented in DebugProbe.AspNetCore

### Local inspection and comparison

- Incoming request and response capture: method, path, URL/query, status, duration, headers and bounded bodies.
- Outgoing HTTP capture through `IHttpClientFactory` during an active captured request, attached to that request's local ID.
- Embedded `/debug` dashboard, request details, outgoing calls, exception grouping and manual trace comparison across applications/environments.
- JSON-aware body differences and displayed request/environment metadata in compare. Cross-service distributed tracing is a separate future capability.
- Bounded, thread-safe in-memory storage controlled by `MaxRequests`; local data lasts for the application process lifetime.

### Configuration and capture controls

- .NET 10 target for package 2.x, dependency injection and startup options validation.
- Custom route prefix and configurable path exclusions, including common health/documentation endpoints by default.
- `MaxBodySizeKb`, `CaptureRequestBody` and `CaptureResponseBody` for incoming and outgoing body capture.
- Configurable header, query-parameter and JSON-field redaction. Header defaults cover Authorization, Cookie and Set-Cookie; query and JSON lists must be configured by the application.
- Dashboard/API endpoints disabled by default in Production, with an optional ASP.NET Core authorization policy for registered DebugProbe endpoints.
- Capture failures isolated from application responses and original exceptions.

### Optional Server export in 2.1.0

- Authenticated export of captured requests, unhandled exceptions and application heartbeats.
- Application/environment identity and a per-process instance ID; scoped ingestion keys issued by Server.
- Existing capture/redaction settings applied to exported data, with the export client excluded from outgoing instrumentation.
- A bounded in-memory queue, one background sender, configurable send timeout and heartbeat interval, throttled warnings and a bounded graceful shutdown drain.
- Best-effort delivery: failed sends and full queues drop events. There are no retries, durable buffers or delivery guarantees in 2.1.0.
- Server export works with the local Production UI disabled and is independent of local history retention.

See the [README](https://github.com/DebugProbe/DebugProbe.AspNetCore/blob/main/README.md) for the current public settings, defaults and migration guidance.

## Implemented Server foundation

These are development capabilities, not a claim that the production release is finished:

- .NET 10 API and central React UI for requests, exceptions, applications and environments.
- PostgreSQL persistence through EF Core with versioned migrations and a data-access boundary that can support a later SQL Server provider.
- ASP.NET Core Identity accounts and cookie login.
- Administrator, Maintainer and Viewer roles, with application/environment access grants enforced by the API.
- Ingestion API keys scoped to an application and environment, stored as hashes, with creation, revocation and rotation.
- UI for managing users, grants and ingestion keys.
- Local Docker development setup with PostgreSQL, bundled UI, persistent database/session-key volumes and automatic development migrations.
- Package-to-server delivery exercised with SampleApi and PostgreSQL; integration checks cover authentication, scope isolation and key lifecycle.

## Next: complete DebugProbe Server

### 1. Make telemetry storage and queries operationally usable

- Add server-side filtering and pagination, replacing the fixed latest-500 read limit.
- Define indexes around measured application/environment, timestamp, status and duration queries.
- Add ingestion payload limits and request validation appropriate for captured bodies and outgoing-call lists.
- Add configurable retention and automatic cleanup of requests, exceptions and heartbeats.
- Make heartbeat freshness and instance state clear in the UI.
- Measure ingestion throughput, concurrent reads, queue drops and database growth under representative workloads before choosing capacity limits.

Start with one PostgreSQL database and a simple deployment. Add storage components or split databases when measurements or a concrete customer requirement justify them.

### 2. Finish customer installation and operation

- Provide production Docker and IIS installation/upgrade instructions, including HTTPS, reverse-proxy settings, persistent session keys and filesystem permissions.
- Define migration execution, upgrade compatibility and recovery procedures.
- Document and exercise backup/restore for telemetry, accounts and session-protection keys.
- Complete account administration needed for customer operation, including password recovery/reset, account disabling and clear access-change behavior.
- Review access to captured bodies and administrative audit history, in addition to existing application/environment grants.
- Exercise authentication, authorization and sensitive-data handling with production deployment settings.

### Server readiness criteria

The first production Server release should be installable and upgradable by a customer, have bounded ingestion/storage behavior, enforce its access rules, and have tested backup/restore and documented failure behavior. The package-to-server path must work in the supported deployment modes.

Completion of these criteria comes before starting the Cloud product. Development Docker setup alone does not establish production readiness.

### Later Server options, driven by customer needs

- Optional Microsoft SQL Server support: provider-specific mappings, migrations, telemetry queries and integration tests, selected at installation. Moving existing data between database engines needs a separate migration procedure.
- Corporate sign-in through the customer's identity provider, while retaining application/environment permissions in Server.
- Improved search, comparison, dependency summaries and team workflows based on actual usage.

SQL Server is a planned extension, not a prerequisite for the first PostgreSQL-based Server release. Other optional features can be prioritized without delaying the Server readiness work above.

## After Server: DebugProbe Cloud

Cloud builds on the Server functionality and package ingestion contract. It adds the requirements of operating a hosted SaaS service:

- Organizations/workspaces and tenant isolation across telemetry, queries, users and ingestion keys.
- Customer onboarding, organization membership, invitations and account lifecycle management.
- Subscription plans, usage measurement, quotas, ingestion rate limits and billing.
- Retention/storage limits per plan, customer data export/deletion and defined data-handling policies.
- Managed provisioning, upgrades, backups, monitoring and recovery, followed by scaling based on measured demand.

The application package should continue to send with scoped ingestion credentials; cloud account and billing operations belong to the hosted service. Hosting vendors, billing providers, subscription prices and infrastructure topology remain decisions for the Cloud phase.

## Package improvements to evaluate alongside product work

These are candidates rather than committed release contents:

- Clearer visibility into export health and dropped-event counts.
- Sampling or selective capture for higher-volume applications.
- Batching or bounded retries if a measured delivery requirement justifies them; define duplicate handling and server compatibility first.
- Activity/OpenTelemetry correlation for cross-service debugging, with a distinction between local request IDs and distributed trace/span IDs.
- Better compare controls for dynamic values, and portable trace export/import.
- Request replay only with deliberate control over authentication, targets and operations that change data.

Package changes must preserve the application response, existing redaction/capture controls and bounded resource usage. Add public extension points when a real integration needs them.

## Engineering principles

- Keep installation, configuration and the code structure simple.
- Prefer built-in .NET/ASP.NET Core features and a small dependency footprint.
- Keep local inspection usable without a Server or Cloud account.
- Apply capture and redaction controls before sending data; enforce authorization and storage limits at the destination.
- Version the ingestion contract and verify compatibility between supported package/server versions.
- Separate implemented behavior from proposals, and update this roadmap when work ships or priorities change.
