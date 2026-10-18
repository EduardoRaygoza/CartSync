using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CartSync.Api.Synchronization;

namespace CartSync.Api.Tests;

public sealed class FoundationEndpointTests : IClassFixture<FoundationApiFactory>
{
    private readonly HttpClient client;

    public FoundationEndpointTests(FoundationApiFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task Bootstrap_requires_authentication()
    {
        var response = await client.GetAsync(
            "/api/v1/sync/bootstrap",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorized_member_receives_terminal_empty_scope()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        var page = await client.GetFromJsonAsync<BootstrapPage>(
            "/api/v1/sync/bootstrap",
            TestContext.Current.CancellationToken);
        Assert.NotNull(page);
        Assert.True(page.Terminal);
        Assert.Equal(0, page.TerminalHouseholdCursor);
        Assert.Empty(page.Projections);
        Assert.Null(page.NextPageToken);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    public async Task Rejected_access_tokens_are_unauthorized(string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(
            "/api/v1/sync/bootstrap",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_access_as_user_scope_is_forbidden()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "missing-scope");
        var response = await client.GetAsync(
            "/api/v1/sync/bootstrap",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Subject_without_active_membership_is_forbidden()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "no-membership");
        var response = await client.GetAsync(
            "/api/v1/sync/bootstrap",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cors_allows_only_the_configured_pwa_origin()
    {
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/v1/sync/bootstrap");
        allowed.Headers.Add("Origin", "https://pwa.example");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var allowedResponse = await client.SendAsync(allowed, TestContext.Current.CancellationToken);
        Assert.Equal("https://pwa.example", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/v1/sync/bootstrap");
        denied.Headers.Add("Origin", "https://other.example");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        var deniedResponse = await client.SendAsync(denied, TestContext.Current.CancellationToken);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Liveness_response_is_redacted()
    {
        var response = await client.GetStringAsync(
            "/health/live",
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("ConnectionStrings", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foundation-subject", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_failure_is_redacted()
    {
        var response = await client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"status\":\"unhealthy\"}", body);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
    }
}
