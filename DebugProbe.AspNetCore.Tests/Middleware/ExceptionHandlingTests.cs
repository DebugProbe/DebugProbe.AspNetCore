using System.Net;
using System.Text;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;

namespace DebugProbe.AspNetCore.Tests.Middleware;

public class ExceptionHandlingTests
{
    [Fact]
    public async Task Captures_exception_response_text()
    {
        await using var app = await CreateExceptionAppAsync("handled error");

        var response = await app.Client.PostAsync("/throw", JsonContent("{\"id\":42}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("handled error", await response.Content.ReadAsStringAsync());
        Assert.Equal("handled error", app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Stores_exception_information_when_exception_is_not_handled()
    {
        await using var app = await DebugProbeTestApp.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/throw", (HttpContext _) => throw new InvalidOperationException("unhandled failure"));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/throw"));

        var entry = app.SingleEntry;
        Assert.Equal(500, entry.StatusCode);
        Assert.Contains("InvalidOperationException", entry.ResponseBody);
        Assert.Contains("unhandled failure", entry.ResponseBody);
    }

    [Fact]
    public async Task Handles_empty_error_responses()
    {
        await using var app = await CreateExceptionAppAsync(string.Empty);

        var response = await app.Client.GetAsync("/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(string.Empty, app.SingleEntry.ResponseBody);
    }

    [Fact]
    public async Task Issue_38_exception_endpoint_captures_status_request_body_and_response_body()
    {
        await using var app = await CreateExceptionAppAsync("{\"error\":\"boom\"}", "application/json");

        var response = await app.Client.PostAsync("/throw", JsonContent("{\"name\":\"Ada\"}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = app.SingleEntry;
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal("{\"name\":\"Ada\"}", entry.RequestBody);
        Assert.Equal("{\"error\":\"boom\"}", entry.ResponseBody);
    }

    private static Task<DebugProbeTestApp> CreateExceptionAppAsync(
        string errorBody,
        string contentType = "text/plain")
    {
        return DebugProbeTestApp.CreateAsync(
            endpoints =>
            {
                endpoints.MapPost("/throw", (HttpContext _) => throw new InvalidOperationException("boom"));
                endpoints.MapGet("/throw", (HttpContext _) => throw new InvalidOperationException("boom"));
            },
            configureAfterDebugProbe: builder =>
            {
                builder.UseExceptionHandler(errorApp =>
                {
                    errorApp.Run(async context =>
                    {
                        _ = context.Features.Get<IExceptionHandlerFeature>();
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = contentType;
                        await context.Response.WriteAsync(errorBody);
                    });
                });
            });
    }

    private static StringContent JsonContent(string value)
    {
        return new StringContent(value, Encoding.UTF8, "application/json");
    }
}
