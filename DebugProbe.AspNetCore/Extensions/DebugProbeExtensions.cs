using System.Net.Http.Json;
using DebugProbe.AspNetCore.Internal;
using DebugProbe.AspNetCore.Middleware;
using DebugProbe.AspNetCore.Models;
using DebugProbe.AspNetCore.Options;
using DebugProbe.AspNetCore.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DebugProbe.AspNetCore.Extensions;

public static class DebugProbeExtensions
{
    private static readonly HttpClient Http = new();

    public static IServiceCollection AddDebugProbe(
        this IServiceCollection services,
        Action<DebugProbeOptions>? configure = null)
    {
        var options = new DebugProbeOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<DebugEntryStore>();

        return services;
    }

    public static IApplicationBuilder UseDebugProbe(this IApplicationBuilder app)
    {
        app.UseMiddleware<DebugProbeMiddleware>();
        app.ApplicationServices.GetRequiredService<DebugEntryStore>();

        if (app is WebApplication webApp)
        {
            webApp.MapGet("/debug", async (HttpContext ctx, DebugEntryStore store) =>
            {
                var items = store.GetAll()
                    .OrderByDescending(x => x.Timestamp)
                    .ToList();

                var html = HtmlRenderer.RenderIndexPage(items);

                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync(html);
            }).ExcludeFromDescription();

            webApp.MapGet("/debug/{id}", async (HttpContext ctx, string id, DebugEntryStore store) =>
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
            }).ExcludeFromDescription();

            webApp.MapGet("/debug/compare/{id}", async (string id, string baseUrl, string remoteTraceId, DebugEntryStore store) =>
            {
                var localEnvironment = store.Environment;
                var localEntry = store.Get(id);
                if (localEntry is null)
                {
                    return Results.NotFound("Local trace not found");
                }


                var normalizedBaseUrl = baseUrl.TrimEnd('/');

                var remoteEnvironmentUrl =
                    $"{normalizedBaseUrl}/debug/environment";

                var remoteEntryUrl =
                    $"{normalizedBaseUrl}/debug/json/{remoteTraceId}";

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
            }).ExcludeFromDescription();

            webApp.MapGet("/debug/environment", (DebugEntryStore store) =>
            {
                return Results.Ok(store.Environment);
            }).ExcludeFromDescription();

            webApp.MapGet("/debug/json/{id}", (string id, DebugEntryStore store) =>
            {
                var item = store.Get(id);
                return item is null ? Results.NotFound() : Results.Json(item);
            }).ExcludeFromDescription();


            webApp.MapGet("/debug/js/{file}", (string file) =>
            {
                if (!EmbeddedResources.JavaScript.TryGetValue(file, out var content))
                {
                    return Results.NotFound();
                }

                return Results.Text(content, "application/javascript");
            }).ExcludeFromDescription();

            webApp.MapPost("/debug/clear", (DebugEntryStore store) =>
            {
                store.Clear();
                return Results.Ok();
            }).ExcludeFromDescription();

            webApp.Map("/debug/logo.png", ctx =>
                WriteEmbeddedAsset(ctx, "DebugProbe.AspNetCore.Assets.debugprobe_logo_white_transparent.png", "image/png")
            ).ExcludeFromDescription();

            webApp.Map("/debug/favicon.ico", ctx =>
                WriteEmbeddedAsset(ctx, "DebugProbe.AspNetCore.Assets.debugprobe_favicon.ico", "image/x-icon")
            ).ExcludeFromDescription();
        }

        return app;
    }

    private static async Task WriteEmbeddedAsset(HttpContext ctx, string resourceName, string contentType)
    {
        ctx.Response.ContentType = contentType;

        var assembly = typeof(DebugProbeMiddleware).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        await stream.CopyToAsync(ctx.Response.Body);
    }
}
