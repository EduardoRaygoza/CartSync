using Microsoft.Data.SqlClient;

internal static class DatabasePrincipalProvisioner
{
    public static async Task RunAsync(SqlConnection connection, string identityName, Guid clientId)
    {
        if (identityName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException("CARTSYNC_API_IDENTITY_NAME contains unsupported characters.");

        var quotedName = $"[{identityName.Replace("]", "]]", StringComparison.Ordinal)}]";
        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{identityName}')
            BEGIN
                DECLARE @sid nvarchar(34) = CONVERT(varchar(34), CONVERT(varbinary(16), @client_id), 1);
                EXEC(N'CREATE USER {quotedName} WITH SID = ' + @sid + N', TYPE = E;');
            END;
            IF IS_ROLEMEMBER(N'cartsync_api', N'{identityName}') <> 1
                ALTER ROLE cartsync_api ADD MEMBER {quotedName};
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@client_id", clientId);
        await command.ExecuteNonQueryAsync();
    }
}
