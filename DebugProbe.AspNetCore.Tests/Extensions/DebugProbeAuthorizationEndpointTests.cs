using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DebugProbe.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Tests.Extensions;

public class DebugProbeAuthorizationEndpointTests
{
    [Fact]
    public async Task Debug_endpoints_do_not_require_authorization_by_default()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")));

        await app.Client.GetAsync("/hello");

        var debugResponse = await app.Client.GetAsync("/debug");
        var jsonResponse = await app.Client.GetAsync($"/debug/json/{app.SingleEntry.Id}");

        Assert.Equal(HttpStatusCode.OK, debugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, jsonResponse.StatusCode);
    }

    [Fact]
    public async Task Debug_endpoints_require_configured_authorization_policy()
    {
        await using var app = await DebugProbeWebApplication.CreateAsync(
            Environments.Development,
            endpoints => endpoints.MapGet("/hello", () => Results.Text("ok")),
            configureServices: services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { });

                services.AddAuthorization(options =>
                {
                    options.AddPolicy("DebugProbePolicy", policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireRole("Admin");
                    });
                });
            },
            configureBeforeDebugProbe: app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            },
            configureUseDebugProbe: options => options.AuthorizationPolicy = "DebugProbePolicy");

        await app.Client.GetAsync("/hello");
        var traceId = app.SingleEntry.Id;

        var unauthorizedDebugResponse = await app.Client.GetAsync("/debug");
        var unauthorizedJsonResponse = await app.Client.GetAsync($"/debug/json/{traceId}");

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/debug");
        authorizedRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "Admin");
        var authorizedDebugResponse = await app.Client.SendAsync(authorizedRequest);

        using var authorizedJsonRequest = new HttpRequestMessage(HttpMethod.Get, $"/debug/json/{traceId}");
        authorizedJsonRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "Admin");
        var authorizedJsonResponse = await app.Client.SendAsync(authorizedJsonRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedDebugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedJsonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorizedDebugResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorizedJsonResponse.StatusCode);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string RoleHeaderName = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeaderName, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(ClaimTypes.Role, role.ToString())
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
