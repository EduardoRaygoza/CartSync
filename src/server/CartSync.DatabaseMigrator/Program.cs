using System.Reflection;
using Microsoft.Data.SqlClient;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CartSync")
    ?? throw new InvalidOperationException("ConnectionStrings__CartSync is required.");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

var assembly = Assembly.GetExecutingAssembly();
var migrations = assembly.GetManifestResourceNames()
    .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
    .Order(StringComparer.Ordinal);

foreach (var migration in migrations)
{
    await using var stream = assembly.GetManifestResourceStream(migration)
        ?? throw new InvalidOperationException($"Missing migration {migration}.");
    using var reader = new StreamReader(stream);
    var script = await reader.ReadToEndAsync();
    foreach (var batch in script.Split("-- CARTSYNC-BATCH", StringSplitOptions.RemoveEmptyEntries))
    {
        await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}

var subject = Environment.GetEnvironmentVariable("FOUNDATION_EXTERNAL_SUBJECT");
if (!string.IsNullOrWhiteSpace(subject))
{
    await SeedFoundationMembership.RunAsync(
        connection,
        subject,
        ParseGuid("FOUNDATION_ACCOUNT_ID"),
        ParseGuid("FOUNDATION_HOUSEHOLD_ID"),
        ParseGuid("FOUNDATION_MEMBER_ID"));
}

var apiIdentityName = Environment.GetEnvironmentVariable("CARTSYNC_API_IDENTITY_NAME");
if (!string.IsNullOrWhiteSpace(apiIdentityName))
{
    await DatabasePrincipalProvisioner.RunAsync(
        connection,
        apiIdentityName,
        ParseGuid("CARTSYNC_API_IDENTITY_CLIENT_ID"));
}

static Guid ParseGuid(string name) => Guid.Parse(
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required for the configured deployment operation."));
