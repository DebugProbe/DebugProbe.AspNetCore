using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DebugProbe.AspNetCore.Tests.Export;

internal sealed record ReceivedEvent(string Kind, string? ApiKey, JsonElement Payload);

// A local HTTP receiver shaped like the server's ingestion endpoints, including controllable failures.
internal sealed class IngestionReceiver : IAsyncDisposable
{
    private readonly WebApplication app;
    public ConcurrentQueue<ReceivedEvent> Events { get; } = new();
    public string Url { get; private set; } = "";
    public Func<HttpContext, Task>? Respond { get; set; }

    private IngestionReceiver(WebApplication app) => this.app = app;

    public static async Task<IngestionReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new IngestionReceiver(app);
        app.MapPost("/prefix/api/ingestion/{kind}", async (HttpContext context, string kind) =>
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            receiver.Events.Enqueue(new(kind, context.Request.Headers["X-DebugProbe-Key"], document.RootElement.Clone()));
            if (receiver.Respond is { } respond) await respond(context);
            else context.Response.StatusCode = 202;
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        receiver.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/prefix";
        return receiver;
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
