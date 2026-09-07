using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.UserProfile.Infrastructure.Persistence;
using Xunit;
using TenantIdHelper = OrderSphere.BuildingBlocks.StronglyTypedIds.TenantId;

namespace OrderSphere.IntegrationTests.Api;

/// <summary>
/// Locks in the request-side half of ADR 0012.
/// <para>
/// The load-bearing claim is that opening the ambient tenant scope once, in
/// <c>RequestContextEnrichmentMiddleware</c>, is sufficient — no command handler needs to assign
/// <c>TenantId</c>, because <c>IntegrationEvent.TenantId</c>'s default reads the ambient slot at
/// construction time and the handler runs inside the request flow. Before that middleware existed
/// the scope was opened on no HTTP path at all, so every API-originated event was staged with
/// <c>Guid.Empty</c> and every downstream service inherited it.
/// </para>
/// </summary>
public sealed class RequestTenantScopeTests : IClassFixture<UserProfileApiFactory>
{
    private const string OrgId = "org_tenant_scope_tests";

    private readonly UserProfileApiFactory _factory;

    public RequestTenantScopeTests(UserProfileApiFactory factory) => _factory = factory;

    private HttpClient Client(string sub, string? org = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        if (org is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.OrgHeader, org);
        return client;
    }

    private async Task<OutboxMessage> StageErasureAndReadOutboxAsync(string sub, string? org)
    {
        var client = Client(sub, org);

        // Auto-provisions the profile, so the erasure command below finds one to anonymize.
        await client.GetAsync("api/v1/profile");

        var response = await client.PostAsJsonAsync("api/v1/profile/erasure-request", new { });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the profile exists at this point");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UserProfileDbContext>();

        var rows = await db.Set<OutboxMessage>()
            .Where(m => m.Content.Contains(sub))
            .ToListAsync();

        return rows.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task Staged_integration_event_carries_the_tenant_from_the_org_claim()
    {
        // The crux: no handler assigns TenantId. If the middleware does not open the scope, the
        // serialized event body carries Guid.Empty and this fails.
        var row = await StageErasureAndReadOutboxAsync("auth0|tenant-scope-org", OrgId);

        var tenantId = JsonDocument.Parse(row.Content)
            .RootElement.GetProperty("TenantId").GetGuid();

        tenantId.Should().Be(TenantIdHelper.FromOrgId(OrgId));
        tenantId.Should().NotBe(TenantIdHelper.Default);
    }

    [Fact]
    public async Task Staged_integration_event_falls_back_to_the_default_tenant_without_an_org_claim()
    {
        // A token with no org_id is the current production state (Auth0 Organizations is not
        // enabled), so this is the path every existing test and deployment takes. It must stay
        // on TenantId.Default rather than deriving something from an absent claim.
        var row = await StageErasureAndReadOutboxAsync("auth0|tenant-scope-no-org", org: null);

        JsonDocument.Parse(row.Content)
            .RootElement.GetProperty("TenantId").GetGuid()
            .Should().Be(TenantIdHelper.Default);
    }

    [Fact]
    public async Task Outbox_row_captures_the_correlation_id_the_gateway_echoed()
    {
        // The Workstream 3 counterpart: the row must carry the correlation id that was ambient
        // when it was written, so the dispatcher can restore it instead of deriving one from the
        // trace id. A client-supplied X-Request-Id is the case the derivation got wrong.
        const string clientId = "client-chosen-correlation-id";

        var client = Client("auth0|tenant-scope-correlation");
        client.DefaultRequestHeaders.Add("X-Request-Id", clientId);

        await client.GetAsync("api/v1/profile");
        await client.PostAsJsonAsync("api/v1/profile/erasure-request", new { });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UserProfileDbContext>();

        var row = await db.Set<OutboxMessage>()
            .Where(m => m.Content.Contains("auth0|tenant-scope-correlation"))
            .SingleAsync();

        row.CorrelationId.Should().Be(clientId);
    }
}
