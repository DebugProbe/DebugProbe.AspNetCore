using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Internal.Export;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Tests.Export;

public class ServerExportTests
{
    internal static DebugProbeServerOptions Server(string url) => new()
    {
        Url = url, ApiKey = "dp_test-key", ApplicationId = "test-app", ApplicationName = "Test app",
        Environment = "Development", Timeout = TimeSpan.FromMilliseconds(250), HeartbeatInterval = TimeSpan.FromSeconds(1),
    };

    internal static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }

    [Fact]
    public async Task Sends_redacted_requests_outgoing_calls_exceptions_and_heartbeats_over_HTTP()
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints =>
            {
                endpoints.MapPost("/hello", async context =>
                {
                    var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("external");
                    using var response = await client.PostAsJsonAsync("http://external.test/hello", new { password = "outgoing-secret" }, context.RequestAborted);
                    await context.Response.WriteAsJsonAsync(new { token = "response-secret", ok = true }, cancellationToken: context.RequestAborted);
                });
                endpoints.MapGet("/fail", (HttpContext _) => throw new InvalidOperationException("test failure"));
                endpoints.MapGet("/health", () => "healthy");
            }, options =>
            {
                options.Server = Server(receiver.Url);
                options.Server.Environment = null; // Use the application's hosting environment.
                options.RedactedJsonFields = ["password", "token"];
                options.RedactedQueryParameters = ["token"];
            }, configureServices: services => services.AddHttpClient("external")
                .ConfigurePrimaryHttpMessageHandler(() => new ExternalHandler()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/hello?token=query-secret");
        request.Headers.Authorization = new("Bearer", "header-secret");
        request.Content = JsonContent.Create(new { password = "body-secret", name = "Alice" });
        Assert.Equal(HttpStatusCode.OK, (await app.Client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        await app.Client.GetAsync("/health", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/fail", TestContext.Current.CancellationToken));
        await Until(() => receiver.Events.Any(e => e.Kind == "exceptions") && receiver.Events.Any(e => e.Kind == "heartbeat"));
        var captured = receiver.Events.Single(e => e.Kind == "requests" && e.Payload.GetProperty("path").GetString() == "/hello");
        Assert.Equal("dp_test-key", captured.ApiKey);
        var body = captured.Payload;
        Assert.Equal(1, body.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("test-app", body.GetProperty("application").GetProperty("applicationId").GetString());
        Assert.Equal("Production", body.GetProperty("application").GetProperty("environment").GetString());
        Assert.Equal(200, body.GetProperty("statusCode").GetInt32());
        Assert.True(body.GetProperty("requestBody").GetProperty("captured").GetBoolean());
        Assert.Contains("[REDACTED]", body.GetProperty("requestBody").GetProperty("content").GetString()!);
        Assert.Single(body.GetProperty("outgoingRequests").EnumerateArray());
        var serialized = body.GetRawText();
        foreach (var secret in new[] { "query-secret", "header-secret", "body-secret", "response-secret", "outgoing-secret" })
            Assert.DoesNotContain(secret, serialized);
        Assert.DoesNotContain(receiver.Events, e => e.Kind == "requests" && e.Payload.GetProperty("path").GetString() == "/health");
        var error = receiver.Events.Single(e => e.Kind == "exceptions").Payload;
        Assert.Equal("System.InvalidOperationException", error.GetProperty("exceptionType").GetString());
        Assert.Equal("test failure", error.GetProperty("message").GetString());
        Assert.Equal(500, error.GetProperty("statusCode").GetInt32());
        Assert.Equal(app.Store.GetAll().Single(e => e.Path == "/fail").Id, error.GetProperty("requestId").GetString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Server_rejections_do_not_change_application_responses_or_retry_events(int status)
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        receiver.Respond = context => { context.Response.StatusCode = status; return Task.CompletedTask; };
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/hello", () => "application works"),
            options => { options.Server = Server(receiver.Url); options.Server.HeartbeatInterval = TimeSpan.FromDays(1); });
        Assert.Equal("application works", await app.Client.GetStringAsync("/hello", TestContext.Current.CancellationToken));
        await Until(() => receiver.Events.Any(e => e.Kind == "requests"));
        // Graceful shutdown drains queued events; no retries should remain afterward.
        await app.DisposeAsync();
        Assert.Single(receiver.Events, e => e.Kind == "requests");
    }

    [Fact]
    public async Task Slow_server_and_full_queue_do_not_block_requests_and_shutdown_is_bounded()
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.Respond = async context =>
        {
            blocked.TrySetResult();
            try { await Task.Delay(TimeSpan.FromSeconds(20), context.RequestAborted); }
            catch (OperationCanceledException) { }
        };
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/hello", () => "ok"), options =>
            {
                options.Server = Server(receiver.Url); options.Server.QueueCapacity = 2;
                options.Server.Timeout = TimeSpan.FromSeconds(1);
                options.Server.HeartbeatInterval = TimeSpan.FromDays(1);
            });
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 25; i++) Assert.Equal("ok", await app.Client.GetStringAsync("/hello", TestContext.Current.CancellationToken));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "Application requests waited for the telemetry server.");
        Assert.Equal(20, app.Store.GetAll().Count);
        await app.DisposeAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(7));
        Assert.InRange(receiver.Events.Count, 1, 3); // One in flight plus the two queued; excess events were dropped.
    }

    [Fact]
    public async Task Redirect_does_not_forward_the_ingestion_key_to_another_server()
    {
        await using var target = await IngestionReceiver.StartAsync();
        await using var redirect = await IngestionReceiver.StartAsync();
        redirect.Respond = context =>
        {
            context.Response.StatusCode = 307;
            context.Response.Headers.Location = target.Url + "/api/ingestion/requests";
            return Task.CompletedTask;
        };
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/hello", () => "ok"), options => options.Server = Server(redirect.Url));
        await app.Client.GetAsync("/hello", TestContext.Current.CancellationToken);
        await Until(() => redirect.Events.Any(e => e.Kind == "requests"));
        await app.DisposeAsync();
        Assert.Empty(target.Events);
    }

    [Fact]
    public async Task Disabled_probe_sends_no_heartbeat_or_requests()
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/hello", () => "ok"),
            options => { options.Enabled = false; options.Server = Server(receiver.Url); });
        Assert.Equal("ok", await app.Client.GetStringAsync("/hello", TestContext.Current.CancellationToken));
        await app.DisposeAsync();
        Assert.Empty(receiver.Events);
    }

    [Fact]
    public async Task Disabled_bodies_do_not_leak_exception_details()
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/fail", (HttpContext _) => throw new InvalidOperationException("private exception detail")),
            options =>
            {
                options.Server = Server(receiver.Url);
                options.CaptureRequestBody = false;
                options.CaptureResponseBody = false;
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/fail", TestContext.Current.CancellationToken));
        await Until(() => receiver.Events.Any(e => e.Kind == "exceptions"));
        var request = receiver.Events.Single(e => e.Kind == "requests").Payload;
        Assert.False(request.GetProperty("responseBody").GetProperty("captured").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, request.GetProperty("responseBody").GetProperty("content").ValueKind);
        Assert.All(receiver.Events, e => Assert.DoesNotContain("private exception detail", e.Payload.GetRawText()));
    }

    [Fact]
    public async Task Periodic_heartbeats_keep_the_same_instance_identity()
    {
        await using var receiver = await IngestionReceiver.StartAsync();
        await using var app = await DebugProbeTestApp.CreateAsync(
            _ => { }, options => { options.Server = Server(receiver.Url); options.Server.Environment = ""; });
        await Until(() => receiver.Events.Count(e => e.Kind == "heartbeat") >= 2);
        var applications = receiver.Events.Where(e => e.Kind == "heartbeat").Select(e => e.Payload.GetProperty("application")).ToArray();
        var instance = applications[0].GetProperty("instanceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instance));
        Assert.All(applications, application =>
        {
            Assert.Equal(instance, application.GetProperty("instanceId").GetString());
            Assert.Equal("", application.GetProperty("environment").GetString());
        });
        Assert.Empty(app.Store.GetAll());
    }

    [Fact]
    public async Task Unreachable_server_does_not_break_the_application()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        await using var app = await DebugProbeTestApp.CreateAsync(
            endpoints => endpoints.MapGet("/hello", () => "still works"), options => options.Server = Server(url));
        Assert.Equal("still works", await app.Client.GetStringAsync("/hello", TestContext.Current.CancellationToken));
        Assert.Single(app.Store.GetAll());
    }

    [Theory]
    [InlineData("ftp://server", "key", "app")]
    [InlineData("http://server?query=1", "key", "app")]
    [InlineData("http://user:password@server", "key", "app")]
    [InlineData("http://server", "", "app")]
    [InlineData("http://server", "key\r\ninjected", "app")]
    [InlineData("http://server", "key", "")]
    public void Invalid_export_configuration_fails_validation(string url, string key, string appId)
    {
        var services = new ServiceCollection();
        services.AddDebugProbe(options => options.Server = new() { Url = url, ApiKey = key, ApplicationId = appId });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<DebugProbeOptions>());
    }

    private sealed class ExternalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token = "outgoing-secret" }) });
    }
}
