using System.Net.Http.Json;
using DebugProbe.AspNetCore.Handlers;
using DebugProbe.AspNetCore.Internal.Compare;
using DebugProbe.AspNetCore.Internal.Rendering;
using DebugProbe.AspNetCore.Internal.Resources;
using DebugProbe.AspNetCore.Internal.Utils;
using DebugProbe.AspNetCore.Middleware;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace DebugProbe.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring DebugProbe.
/// </summary>
public static class DebugProbeExtensions
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// Enables the DebugProbe middleware and dashboard.
    /// </summary>
    public static IServiceCollection AddDebugProbe(this IServiceCollection services, Action<DebugProbeOptions>? configure = null)
    {
        var options = new DebugProbeOptions();

        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddSingleton<DebugEntryStore>();

        services.AddHttpContextAccessor();

        services.AddHttpClient();

        if (options.CaptureOutgoingHttpClientRequests)
        {
            services.AddTransient<DebugProbeHttpClientHandler>();

            services.ConfigureAll<HttpClientFactoryOptions>(httpClientOptions =>
            {
                httpClientOptions.HttpMessageHandlerBuilderActions.Add(builder =>
                {
                    builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<DebugProbeHttpClientHandler>());
                });
            });
        }

        return services;
    }

    /// <summary>
    /// Registers DebugProbe services.
    /// </summary>
    public static IApplicationBuilder UseDebugProbe(this IApplicationBuilder app)
    {
        return app.UseDebugProbe(configure: null);
    }

    /// <summary>
    /// Registers DebugProbe services and configures runtime endpoint options.
    /// </summary>
    public static IApplicationBuilder UseDebugProbe(this IApplicationBuilder app, Action<DebugProbeOptions>? configure)
    {
        var options = app.ApplicationServices.GetRequiredService<DebugProbeOptions>();
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        configure?.Invoke(options);

        options.AllowLocalCompareTargets ??= environment.IsDevelopment();

        app.UseMiddleware<DebugProbeMiddleware>();
        app.ApplicationServices.GetRequiredService<DebugEntryStore>();

        if (app is WebApplication webApp)
        {
            if (ShouldMapUiEndpoints(environment, options))
            {
                RequireDebugAuthorization(webApp.MapGet("/debug", async (HttpContext ctx, DebugEntryStore store) =>
                {
                    var items = store.GetAll()
                        .OrderByDescending(x => x.Timestamp)
                        .ToList();

                    var html = HtmlRenderer.RenderIndexPage(items);
                    ctx.Response.ContentType = "text/html";

                    await ctx.Response.WriteAsync(html);

                }).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.MapGet("/debug/{id}", async (HttpContext ctx, string id, DebugEntryStore store) =>
                {
                    var item = store.Get(id);

                    if (item is null)
                    {
                        ctx.Response.StatusCode = 404;
                        await ctx.Response.WriteAsync("Not found");
                        return;
                    }

                    var prettyRequest = JsonUtils.Format(item.RequestBody);
                    var prettyResponse = JsonUtils.Format(item.ResponseBody);

                    var html = HtmlRenderer.RenderDetailsPage(item, store.Environment, prettyRequest, prettyResponse);
                    ctx.Response.ContentType = "text/html";

                    await ctx.Response.WriteAsync(html);

                }).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.MapGet("/compare", (string? baseUrl, string? traceId, string? localTraceId) =>
                {
                    if (string.IsNullOrWhiteSpace(localTraceId))
                    {
                        return Results.BadRequest("Missing local trace id");
                    }

                    var html = HtmlRenderer.RenderComparePage(localTraceId, baseUrl ?? "", traceId ?? "");

                    return Results.Content(html, "text/html");

                }).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.MapGet("/debug/js/{file}", (string file) =>
                {
                    if (!EmbeddedResources.JavaScript.TryGetValue(file, out var content))
                    {
                        return Results.NotFound();
                    }

                    return Results.Text(content, "application/javascript");

                }).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.MapPost("/debug/clear", (DebugEntryStore store) =>
                {
                    store.Clear();

                    return Results.Ok();

                }).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.Map("/debug/logo.png", ctx =>
                    EmbeddedAssetWriter.WriteEmbeddedAsset(ctx, "DebugProbe.AspNetCore.Assets.images.debugprobe_logo_white_transparent.png", "image/png")
                ).ExcludeFromDescription(), options);

                RequireDebugAuthorization(webApp.Map("/debug/favicon.ico", ctx =>
                    EmbeddedAssetWriter.WriteEmbeddedAsset(ctx, "DebugProbe.AspNetCore.Assets.images.debugprobe_favicon.ico", "image/x-icon")
                ).ExcludeFromDescription(), options);
            }

            RequireDebugAuthorization(webApp.MapGet("/debug/compare/{id}", async (string id, string baseUrl, string remoteTraceId,
                DebugEntryStore store,
                DebugProbeOptions options) =>
            {
                var localEnvironment = store.Environment;
                var localEntry = store.Get(id);

                if (localEntry is null)
                {
                    return Results.NotFound("Local trace not found");
                }

                if (!Guid.TryParse(remoteTraceId, out _))
                {
                    return Results.BadRequest("Invalid remote trace id");
                }

                var validation = await CompareUrlValidator.ValidateCompareBaseUrlAsync(baseUrl, options);

                if (!validation.IsValid)
                {
                    return Results.BadRequest(validation.Error);
                }

                var remoteEnvironmentUrl = new Uri(validation.BaseUri!, "/debug/environment");

                var remoteEntryUrl = new Uri(validation.BaseUri!, $"/debug/json/{remoteTraceId}");

                DebugEntry? remoteEntry;
                DebugEnvironment? remoteEnvironment;

                try
                {
                    remoteEnvironment = await Http.GetFromJsonAsync<DebugEnvironment>(remoteEnvironmentUrl);

                    if (remoteEnvironment is null)
                    {
                        return Results.BadRequest("Failed to load remote environment");
                    }

                    remoteEntry = await Http.GetFromJsonAsync<DebugEntry>(remoteEntryUrl);

                    if (remoteEntry is null)
                    {
                        return Results.NotFound("Remote trace not found");
                    }
                }
                catch
                {
                    return Results.BadRequest("Failed to reach remote server");
                }
               
                var diff = DebugEntryComparer.Compare(localEntry, remoteEntry);

                return Results.Ok(new
                {
                    localTrace = localEntry,
                    remoteTrace = remoteEntry,
                    localEnvironment,
                    remoteEnvironment,
                    method = new { local = localEntry.Method, remote = remoteEntry.Method },
                    path = new { local = localEntry.Path, remote = remoteEntry.Path },
                    status = new { local = localEntry.StatusCode, remote = remoteEntry.StatusCode },

                    requestTime = new
                    {
                        local = localEntry.RequestTimeUtc.ToLocalTime().ToString("HH:mm:ss"),
                        remote = remoteEntry.RequestTimeUtc.ToLocalTime().ToString("HH:mm:ss"),
                    },

                    environment = new { local = localEnvironment.Environment, remote = remoteEnvironment?.Environment ?? "" },
                    culture = new { local = localEnvironment.Culture, remote = remoteEnvironment?.Culture ?? "" },
                    requestBody = new { local = localEntry.RequestBody ?? "", remote = remoteEntry.RequestBody ?? "" },
                    responseBody = new { local = localEntry.ResponseBody ?? "", remote = remoteEntry.ResponseBody ?? "" },
                    diffs = diff
                });

            }).ExcludeFromDescription(), options);

            RequireDebugAuthorization(webApp.MapGet("/debug/environment", (DebugEntryStore store) =>
            {
                return Results.Ok(store.Environment);

            }).ExcludeFromDescription(), options);

            RequireDebugAuthorization(webApp.MapGet("/debug/json/{id}", (string id, DebugEntryStore store) =>
            {
                var item = store.Get(id);

                return item is null ? Results.NotFound() : Results.Json(item);

            }).ExcludeFromDescription(), options);


        }

        return app;
    }

    private static bool ShouldMapUiEndpoints(IHostEnvironment environment, DebugProbeOptions options)
    {
        return !environment.IsProduction() || options.AllowUiInProduction;
    }

    private static void RequireDebugAuthorization(IEndpointConventionBuilder endpoint, DebugProbeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            endpoint.RequireAuthorization(options.AuthorizationPolicy);
        }
    }
}
