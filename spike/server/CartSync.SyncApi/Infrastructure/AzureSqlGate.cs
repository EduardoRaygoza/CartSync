using Microsoft.Data.SqlClient;

namespace CartSync.SyncApi.Infrastructure;

public static class AzureSqlGate
{
    public const string ApplicationLockProcedure = "sys.sp_getapplock";
    public const string SessionContextFunction = "SESSION_CONTEXT";

    public static async Task<long> ExecuteSerializedAsync(
        SqlConnection connection,
        Guid householdId,
        Guid memberId,
        Func<SqlConnection, SqlTransaction, Task> mutation,
        CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await SetContext(connection, transaction, "household_id", householdId, cancellationToken);
            await SetContext(connection, transaction, "member_id", memberId, cancellationToken);
            await using var lockCommand = new SqlCommand("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource, 'Exclusive', 'Transaction', @Timeout; SELECT @result;", connection, transaction);
            lockCommand.Parameters.AddWithValue("@Resource", $"cartsync:household:{householdId:D}");
            lockCommand.Parameters.AddWithValue("@Timeout", 5_000);
            var lockResult = Convert.ToInt32(await lockCommand.ExecuteScalarAsync(cancellationToken));
            if (lockResult < 0) throw new InvalidOperationException($"sp_getapplock failed with code {lockResult}");

            await mutation(connection, transaction);
            await using var cursorCommand = new SqlCommand("UPDATE dbo.HouseholdCursor WITH (UPDLOCK) SET NextCursor = NextCursor + 1 OUTPUT inserted.NextCursor WHERE HouseholdId = @household;", connection, transaction);
            cursorCommand.Parameters.AddWithValue("@household", householdId);
            var cursor = Convert.ToInt64(await cursorCommand.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return cursor;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            // The gate owns the connection during this transaction. Values stay private to the
            // reducer and are cleared before the connection can return to the pool.
            await ClearContext(connection, "member_id");
            await ClearContext(connection, "household_id");
        }
    }

    private static async Task SetContext(SqlConnection connection, SqlTransaction transaction, string key, Guid value, CancellationToken token)
    {
        await using var command = new SqlCommand("EXEC sys.sp_set_session_context @key, @value, 0;", connection, transaction);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task ClearContext(SqlConnection connection, string key)
    {
        if (connection.State != System.Data.ConnectionState.Open) return;
        await using var command = new SqlCommand("EXEC sys.sp_set_session_context @key, NULL;", connection);
        command.Parameters.AddWithValue("@key", key);
        try { await command.ExecuteNonQueryAsync(); } catch (SqlException) { SqlConnection.ClearPool(connection); }
    }
}
