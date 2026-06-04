using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DebugProbe.AspNetCore.Tests.Infrastructure;

internal sealed class DebugProbeWebApplication : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DebugProbeWebApplication(WebApplication app)
    {
        _app = app;
        Client = app.GetTestClient();
        Store = app.Services.GetRequiredService<DebugEntryStore>();
    }

    public HttpClient Client { get; }

    public DebugEntryStore Store { get; }

    public DebugEntry SingleEntry => Assert.Single(Store.GetAll());

    public static async Task<DebugProbeWebApplication> CreateAsync(
        string environmentName,
        Action<IEndpointRouteBuilder>? mapEndpoints = null,
        Action<DebugProbeOptions>? configureOptions = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });

        builder.WebHost.UseTestServer();

        builder.Services.AddRouting();
        builder.Services.AddDebugProbe(configureOptions);

        var app = builder.Build();

        app.UseRouting();
        app.UseDebugProbe();

        mapEndpoints?.Invoke(app);

        await app.StartAsync();

        return new DebugProbeWebApplication(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
