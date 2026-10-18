using System.Data;
using Microsoft.Data.SqlClient;

namespace CartSync.Api.IdentityAccess;

public sealed record HouseholdExecutionContext(Guid MemberId, Guid HouseholdId);

public interface IAuthorizedHouseholdSql
{
    Task<T?> ExecuteAsync<T>(
        string externalSubject,
        Func<SqlConnection, SqlTransaction, HouseholdExecutionContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}

public sealed class AuthorizedHouseholdSql(
    IConfiguration configuration,
    ILogger<AuthorizedHouseholdSql> logger) : IAuthorizedHouseholdSql
{
    private readonly string connectionString = configuration.GetConnectionString("CartSync")
        ?? throw new InvalidOperationException("ConnectionStrings:CartSync is required.");

    public async Task<T?> ExecuteAsync<T>(
        string externalSubject,
        Func<SqlConnection, SqlTransaction, HouseholdExecutionContext, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var context = await ResolveMembershipAsync(connection, transaction, externalSubject, cancellationToken);
        if (context is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return default;
        }

        var sessionContextSet = false;
        try
        {
            await SetSessionContextAsync(connection, transaction, context, cancellationToken);
            sessionContextSet = true;
            var result = await action(connection, transaction, context, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackError) { logger.LogWarning(rollbackError, "Database transaction rollback failed."); }
            throw;
        }
        finally
        {
            if (sessionContextSet)
            {
                try { await ClearSessionContextAsync(connection, CancellationToken.None); }
                catch (Exception cleanupError)
                {
                    logger.LogError(cleanupError, "Database session-context cleanup failed; clearing the connection pool.");
                    SqlConnection.ClearPool(connection);
                }
            }
        }
    }

    private static async Task<HouseholdExecutionContext?> ResolveMembershipAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string externalSubject,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT m.public_id, h.public_id
            FROM dbo.accounts AS a
            INNER JOIN dbo.household_memberships AS m ON m.account_id = a.id
            INNER JOIN dbo.households AS h ON h.id = m.household_id
            WHERE a.external_subject = @external_subject
              AND a.status = N'active'
              AND m.status = N'active'
              AND h.status = N'active';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@external_subject", SqlDbType.NVarChar, 255).Value = externalSubject;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new HouseholdExecutionContext(reader.GetGuid(0), reader.GetGuid(1))
            : null;
    }

    private static async Task SetSessionContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        HouseholdExecutionContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            EXEC sys.sp_set_session_context @key=N'member_id', @value=@member_id;
            EXEC sys.sp_set_session_context @key=N'household_id', @value=@household_id;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@member_id", SqlDbType.UniqueIdentifier).Value = context.MemberId;
        command.Parameters.Add("@household_id", SqlDbType.UniqueIdentifier).Value = context.HouseholdId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ClearSessionContextAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            EXEC sys.sp_set_session_context @key=N'member_id', @value=NULL;
            EXEC sys.sp_set_session_context @key=N'household_id', @value=NULL;
            """;
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
