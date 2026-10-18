using Microsoft.Data.SqlClient;

internal static class SeedFoundationMembership
{
    public static async Task RunAsync(
        SqlConnection connection,
        string externalSubject,
        Guid accountId,
        Guid householdId,
        Guid memberId)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            IF NOT EXISTS (SELECT 1 FROM dbo.accounts WHERE external_subject = @subject)
                INSERT dbo.accounts(public_id, external_subject, status) VALUES (@account_id, @subject, N'active');

            DECLARE @account_key bigint = (SELECT id FROM dbo.accounts WHERE external_subject = @subject);
            IF NOT EXISTS (SELECT 1 FROM dbo.households WHERE public_id = @household_id)
                INSERT dbo.households(public_id, status) VALUES (@household_id, N'active');

            DECLARE @household_key bigint = (SELECT id FROM dbo.households WHERE public_id = @household_id);
            IF NOT EXISTS (SELECT 1 FROM dbo.household_memberships WHERE account_id = @account_key)
                INSERT dbo.household_memberships(public_id, account_id, household_id, role, status)
                VALUES (@member_id, @account_key, @household_key, N'owner', N'active');

            IF NOT EXISTS (SELECT 1 FROM dbo.household_cursors WHERE household_id = @household_id)
                INSERT dbo.household_cursors(household_id, [cursor]) VALUES (@household_id, 0);

            COMMIT TRANSACTION;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@subject", externalSubject);
        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@household_id", householdId);
        command.Parameters.AddWithValue("@member_id", memberId);
        await command.ExecuteNonQueryAsync();
    }
}
