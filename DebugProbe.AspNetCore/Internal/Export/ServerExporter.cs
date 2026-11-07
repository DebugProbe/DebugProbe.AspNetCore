using System.Net.Http.Json;
using System.Threading.Channels;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DebugProbe.AspNetCore.Internal.Export;

internal sealed class ServerExporter : BackgroundService
{
    internal const string ClientName = "DebugProbe.ServerExport";
    private readonly DebugProbeOptions options;
    private readonly DebugEntryStore store;
    private readonly IHttpClientFactory clients;
    private readonly ILogger<ServerExporter> logger;
    private readonly TelemetryPayload payloads;
    private readonly Channel<ExportMessage> queue;
    private readonly CancellationTokenSource heartbeatStop = new();
    private long nextWarning;

    public ServerExporter(DebugProbeOptions options, DebugEntryStore store, IHostEnvironment host,
        IHttpClientFactory clients, ILogger<ServerExporter> logger)
    {
        this.options = options;
        this.store = store;
        this.clients = clients;
        this.logger = logger;

        payloads = new TelemetryPayload(options, host, store.Environment);
        
        queue = Channel.CreateBounded<ExportMessage>(new BoundedChannelOptions(options.Enabled ? options.Server?.QueueCapacity ?? 256 : 256)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait,
        });
        
        if (options.Enabled && options.Server is not null)
        {
            store.EntryAdded += OnEntry;
        }
    }

    private void OnEntry(DebugEntry entry)
    {
        try
        {
            Enqueue(payloads.Request(entry));
            if (payloads.Exception(entry) is { } exception) Enqueue(exception);
        }
        catch (Exception) 
        { 
            Warn("Unable to prepare telemetry; event dropped."); 
        }
    }

    private void Enqueue(ExportMessage message)
    {
        if (!queue.Writer.TryWrite(message))
        {
            Warn("Telemetry queue is full or stopping; event dropped.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || options.Server is null)
        {
            return;
        }

        using var client = clients.CreateClient(ClientName);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, heartbeatStop.Token);
        var heartbeat = HeartbeatsAsync(heartbeatCancellation.Token);
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, message.Endpoint) { Content = JsonContent.Create(message.Payload) };
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
                    if (!response.IsSuccessStatusCode) Warn($"Telemetry rejected (HTTP {(int)response.StatusCode}); event dropped. Check server URL, API key and scope.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) 
                { 
                    break; 
                }
                catch (Exception) 
                { 
                    Warn("Telemetry send failed or timed out; event dropped."); 
                }
            }
        }
        finally
        {
            heartbeatStop.Cancel();
            try 
            { 
                await heartbeat; 
            }
            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested) { }
        }
    }

    private async Task HeartbeatsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Server!.HeartbeatInterval);
        do { Enqueue(payloads.Heartbeat()); } while (await timer.WaitForNextTickAsync(ct));
    }

    private void Warn(string message)
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref nextWarning);
        if (now >= previous && Interlocked.CompareExchange(ref nextWarning, now + TimeSpan.FromSeconds(30).Ticks, previous) == previous)
        {
            try 
            {
                logger.LogWarning("DebugProbe export: {Message} Further warnings are suppressed for 30 seconds.", message);
            }
            catch (Exception)
            { 
                /* A logging provider must not turn a telemetry failure into a host failure. */ 
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        store.EntryAdded -= OnEntry;
        heartbeatStop.Cancel();
        queue.Writer.TryComplete();
        
        // Drain on graceful shutdown, but never hold the application open indefinitely.
        if (ExecuteTask is not null)
        {
            try 
            { 
                await ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        store.EntryAdded -= OnEntry;
        base.Dispose();
        heartbeatStop.Dispose();
    }
}
