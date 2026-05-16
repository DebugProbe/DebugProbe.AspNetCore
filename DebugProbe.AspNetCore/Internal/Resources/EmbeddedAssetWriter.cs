using DebugProbe.AspNetCore.Middleware;
using Microsoft.AspNetCore.Http;

namespace DebugProbe.AspNetCore.Internal.Resources;

internal static class EmbeddedAssetWriter
{
    public static async Task WriteEmbeddedAsset(HttpContext ctx, string resourceName, string contentType)
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
