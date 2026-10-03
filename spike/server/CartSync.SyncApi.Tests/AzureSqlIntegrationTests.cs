using CartSync.SyncApi.Infrastructure;
using Microsoft.Data.SqlClient;

namespace CartSync.SyncApi.Tests;

public sealed class AzureSqlIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("CARTSYNC_SQL_CONNECTION");

    [Fact]
    public async Task Household_lock_serializes_cursors_and_rollbacks_do_not_advance_them()
    {
        if (ConnectionString is null) return;
        var household = Guid.NewGuid();
        var member = Guid.NewGuid();
        await SeedCursor(household);
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            return await AzureSqlGate.ExecuteSerializedAsync(connection, household, member, (_, _) => Task.CompletedTask, CancellationToken.None);
        });
        var cursors = await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (long)value), cursors.Order());

        await using var failedConnection = new SqlConnection(ConnectionString);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AzureSqlGate.ExecuteSerializedAsync(
            failedConnection, household, member, (_, _) => throw new InvalidOperationException("rollback"), CancellationToken.None));
        await using var finalConnection = new SqlConnection(ConnectionString);
        var final = await AzureSqlGate.ExecuteSerializedAsync(finalConnection, household, member, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(13, final);
    }

    [Fact]
    public async Task Session_context_does_not_leak_through_the_connection_pool()
    {
        if (ConnectionString is null) return;
        var household = Guid.NewGuid();
        await SeedCursor(household);
        await using (var used = new SqlConnection(ConnectionString))
            await AzureSqlGate.ExecuteSerializedAsync(used, household, Guid.NewGuid(), (_, _) => Task.CompletedTask, CancellationToken.None);

        await using var reused = new SqlConnection(ConnectionString);
        await reused.OpenAsync();
        await using var command = new SqlCommand("SELECT SESSION_CONTEXT(N'household_id'), SESSION_CONTEXT(N'member_id');", reused);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.IsDBNullAsync(0));
        Assert.True(await reader.IsDBNullAsync(1));
    }

    private static async Task SeedCursor(Guid household)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("INSERT INTO dbo.HouseholdCursor(HouseholdId, NextCursor) VALUES (@id, 0);", connection);
        command.Parameters.AddWithValue("@id", household);
        await command.ExecuteNonQueryAsync();
    }
}
