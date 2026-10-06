using System.Security.Claims;
using System.Text.Encodings.Web;
using CartSync.Api.IdentityAccess;
using CartSync.Api.Synchronization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace CartSync.Api.Tests;

public sealed class FoundationApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Cors:AllowedOrigin", "https://pwa.example");
        builder.UseSetting(
            "ConnectionStrings:CartSync",
            "Server=127.0.0.1,1;Database=CartSync;User Id=unavailable;Encrypt=False;Connect Timeout=1");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthorizedHouseholdSql>();
            services.AddScoped<IAuthorizedHouseholdSql, FakeAuthorizedHouseholdSql>();
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class FakeAuthorizedHouseholdSql : IAuthorizedHouseholdSql
{
    public Task<T?> ExecuteAsync<T>(
        string externalSubject,
        Func<SqlConnection, SqlTransaction, HouseholdExecutionContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (externalSubject == "no-membership") return Task.FromResult<T?>(default);
        object page = new BootstrapPage([], [], [], true, 0, null);
        return Task.FromResult<T?>((T)page);
    }
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FoundationTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorization))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = authorization.ToString().Replace("Bearer ", string.Empty, StringComparison.Ordinal);
        if (token is "invalid" or "expired" or "wrong-audience")
            return Task.FromResult(AuthenticateResult.Fail("The access token is invalid."));

        var claims = new List<Claim>
        {
            new("oid", token == "no-membership" ? "no-membership" : "foundation-subject"),
        };
        if (token != "missing-scope") claims.Add(new Claim("scp", "access_as_user"));
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
