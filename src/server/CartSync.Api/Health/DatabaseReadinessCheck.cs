using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CartSync.Api.Health;

public sealed class DatabaseReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    private readonly string connectionString = configuration.GetConnectionString("CartSync")
        ?? throw new InvalidOperationException("ConnectionStrings:CartSync is required.");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database connectivity failed.", exception);
        }
    }
}
