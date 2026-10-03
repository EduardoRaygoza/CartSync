namespace CartSync.SyncApi.Tests;

public sealed class SchemaContractTests
{
    private static string Schema => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql"));

    [Theory]
    [InlineData("sp_getapplock")]
    [InlineData("SESSION_CONTEXT")]
    [InlineData("CREATE SECURITY POLICY")]
    [InlineData("IDENTITY(1,1)")]
    [InlineData("Latin1_General_100_BIN2")]
    [InlineData("OperationReceipt")]
    [InlineData("FieldVersion")]
    [InlineData("IdentityAlias")]
    [InlineData("Tombstone")]
    [InlineData("SyncIssue")]
    public void Azure_sql_contract_contains_required_gate(string requiredToken) => Assert.Contains(requiredToken, Schema);
}
