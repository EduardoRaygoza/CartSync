using CartSync.Api.Health;
using CartSync.Api.IdentityAccess;
using CartSync.Api.Synchronization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}
builder.Services.AddOpenApi();
builder.Services
    .AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("ExternalId"));
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("access_as_user", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context =>
        {
            var scopes = context.User.FindFirst("scp")?.Value
                ?? context.User.FindFirst("scope")?.Value
                ?? string.Empty;
            return scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("access_as_user", StringComparer.Ordinal);
        }));
});

var allowedOrigin = builder.Configuration["Cors:AllowedOrigin"]
    ?? throw new InvalidOperationException("Cors:AllowedOrigin is required.");
builder.Services.AddCors(options => options.AddPolicy("pwa", policy => policy
    .WithOrigins(allowedOrigin)
    .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
    .WithHeaders("Authorization", "Content-Type")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuthorizedHouseholdSql, AuthorizedHouseholdSql>();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", tags: ["ready"]);

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors("pwa");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = RedactedHealthResponse.WriteAsync,
});
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapBootstrapEndpoint();

app.Run();

public partial class Program;
