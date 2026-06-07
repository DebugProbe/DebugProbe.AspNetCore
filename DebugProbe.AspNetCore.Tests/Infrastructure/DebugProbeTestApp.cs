using DebugProbe.AspNetCore.Extensions;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DebugProbe.AspNetCore.Tests.Infrastructure;

internal sealed class DebugProbeTestApp : IAsyncDisposable
{
    private readonly IHost _host;

    private DebugProbeTestApp(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
        Store = host.Services.GetRequiredService<DebugEntryStore>();
    }

    public HttpClient Client { get; }

    public DebugEntryStore Store { get; }

    public DebugEntry SingleEntry => Assert.Single(Store.GetAll());

    public static async Task<DebugProbeTestApp> CreateAsync(
        Action<IEndpointRouteBuilder> mapEndpoints,
        Action<DebugProbeOptions>? configureOptions = null,
        Action<IApplicationBuilder>? configureAfterDebugProbe = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDebugProbe(configureOptions);
                    configureServices?.Invoke(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseDebugProbe();
                    configureAfterDebugProbe?.Invoke(app);
                    app.UseEndpoints(mapEndpoints);
                });
            })
            .StartAsync();

        return new DebugProbeTestApp(host);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
