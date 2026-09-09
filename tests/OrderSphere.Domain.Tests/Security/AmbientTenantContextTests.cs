using FluentAssertions;
using OrderSphere.BuildingBlocks.Security;
using Xunit;

namespace OrderSphere.Domain.Tests.Security;

/// <summary>
/// The ambient tenant slot underpins four separate mechanisms — log enrichment, EF audit
/// stamping, the tenant query filter, and <c>IntegrationEvent.TenantId</c>'s default — so its
/// scoping behaviour is load-bearing well beyond diagnostics. These mirror
/// <see cref="AmbientCorrelationContextTests"/>.
/// </summary>
public sealed class AmbientTenantContextTests
{
    [Fact]
    public void Ambient_is_null_outside_any_scope()
    {
        AmbientTenantContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void Scope_sets_and_restores_the_ambient_value()
    {
        var tenant = Guid.NewGuid();

        using (AmbientTenantContext.BeginScope(tenant))
        {
            AmbientTenantContext.Ambient.Should().Be(tenant);
        }

        AmbientTenantContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void Nested_scope_restores_the_previous_value_not_null()
    {
        // A request scope (opened by RequestContextEnrichmentMiddleware) can enclose a nested
        // operation. If the inner scope restored null instead of the outer tenant, the remainder
        // of the request would silently read and write the default tenant.
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (AmbientTenantContext.BeginScope(outer))
        {
            using (AmbientTenantContext.BeginScope(inner))
            {
                AmbientTenantContext.Ambient.Should().Be(inner);
            }

            AmbientTenantContext.Ambient.Should().Be(outer);
        }
    }

    [Fact]
    public async Task Scope_does_not_leak_into_a_parallel_flow()
    {
        Guid? observed = Guid.Empty;

        var other = Task.Run(async () =>
        {
            await Task.Yield();
            observed = AmbientTenantContext.Ambient;
        });

        using (AmbientTenantContext.BeginScope(Guid.NewGuid()))
        {
            await other;
        }

        observed.Should().BeNull();
    }
}
