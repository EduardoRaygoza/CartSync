using System.Data;
using System.Security.Claims;
using CartSync.Api.IdentityAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CartSync.Api.Synchronization;

public sealed record BootstrapPage(
    IReadOnlyList<object> Projections,
    IReadOnlyList<object> Aliases,
    IReadOnlyList<object> SyncIssues,
    bool Terminal,
    long? TerminalHouseholdCursor,
    string? NextPageToken);

public static class BootstrapEndpoint
{
    public static IEndpointRouteBuilder MapBootstrapEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/sync/bootstrap", HandleAsync)
            .RequireAuthorization("access_as_user")
            .Produces<BootstrapPage>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("getBootstrapPage")
            .WithTags("Synchronization")
            .WithSummary("Page the authorized automatic synchronization scope");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IAuthorizedHouseholdSql database,
        CancellationToken cancellationToken,
        [FromQuery] string? pageToken,
        [FromQuery] int pageSize = 200)
    {
        _ = pageToken;
        if (pageSize is < 1 or > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(pageSize)] = ["Page size must be between 1 and 500."],
            });

        var subject = principal.FindFirstValue("oid") ?? principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();

        var page = await database.ExecuteAsync(
            subject,
            static async (connection, transaction, _, token) =>
            {
                const string sql = "SELECT [cursor] FROM dbo.household_cursors;";
                await using var command = new SqlCommand(sql, connection, transaction);
                var value = await command.ExecuteScalarAsync(token);
                var cursor = value is null or DBNull ? 0L : Convert.ToInt64(value);
                return new BootstrapPage([], [], [], true, cursor, null);
            },
            cancellationToken);

        return page is null
            ? Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Household access is not authorized.",
                type: "https://cartsync.app/problems/forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = "forbidden" })
            : Results.Ok(page);
    }
}
