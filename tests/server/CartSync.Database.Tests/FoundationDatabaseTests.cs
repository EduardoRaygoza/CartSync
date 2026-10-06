using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace CartSync.Database.Tests;

public sealed class FoundationDatabaseTests : IAsyncLifetime
{
    private readonly MsSqlContainer sql = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090").Build();

    public ValueTask InitializeAsync() => new(sql.StartAsync());
    public ValueTask DisposeAsync() => new(sql.DisposeAsync().AsTask());

    [Fact]
    public async Task Migration_is_idempotent_and_rls_is_household_scoped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ApplyMigrationsAsync(cancellationToken);
        await ApplyMigrationsAsync(cancellationToken);

        var householdA = Guid.NewGuid();
        var householdB = Guid.NewGuid();
        var pooledConnectionString = new SqlConnectionStringBuilder(sql.GetConnectionString())
        {
            MaxPoolSize = 1,
        }.ConnectionString;
        await using var connection = new SqlConnection(pooledConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var seed = new SqlCommand(
            "INSERT dbo.household_cursors(household_id, [cursor]) VALUES (@a, 3), (@b, 7);", connection))
        {
            seed.Parameters.AddWithValue("@a", householdA);
            seed.Parameters.AddWithValue("@b", householdB);
            await seed.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var context = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'household_id', @value=@household_id;", connection))
        {
            context.Parameters.AddWithValue("@household_id", householdA);
            await context.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var query = new SqlCommand("SELECT [cursor] FROM dbo.household_cursors;", connection))
        {
            Assert.Equal(3L, Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken)));
        }

        await using (var clear = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'household_id', @value=NULL;", connection))
        {
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }
        await connection.CloseAsync();

        await using var reusedConnection = new SqlConnection(pooledConnectionString);
        await reusedConnection.OpenAsync(cancellationToken);
        await using (var noLeak = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.household_cursors;", reusedConnection))
        {
            Assert.Equal(0L, Convert.ToInt64(await noLeak.ExecuteScalarAsync(cancellationToken)));
        }
        await using (var context = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'household_id', @value=@household_id;", reusedConnection))
        {
            context.Parameters.AddWithValue("@household_id", householdB);
            await context.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var householdBQuery = new SqlCommand("SELECT [cursor] FROM dbo.household_cursors;", reusedConnection);
        Assert.Equal(7L, Convert.ToInt64(await householdBQuery.ExecuteScalarAsync(cancellationToken)));
    }

    [Fact]
    public async Task Api_role_cannot_run_schema_migrations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await ApplyMigrationsAsync(cancellationToken);

        await using var connection = new SqlConnection(sql.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using (var createPrincipal = new SqlCommand("""
            CREATE USER cartsync_api_test WITHOUT LOGIN;
            ALTER ROLE cartsync_api ADD MEMBER cartsync_api_test;
            """, connection))
        {
            await createPrincipal.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var forbiddenMigration = new SqlCommand("""
            EXECUTE AS USER = N'cartsync_api_test';
            CREATE TABLE dbo.api_must_not_migrate(id int NOT NULL);
            """, connection);
        await Assert.ThrowsAsync<SqlException>(async () =>
            await forbiddenMigration.ExecuteNonQueryAsync(cancellationToken));
    }

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(sql.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        var assembly = typeof(SeedFoundationMembership).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql")).Order())
        {
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync(cancellationToken);
            foreach (var batch in script.Split("-- CARTSYNC-BATCH", StringSplitOptions.RemoveEmptyEntries))
            {
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
