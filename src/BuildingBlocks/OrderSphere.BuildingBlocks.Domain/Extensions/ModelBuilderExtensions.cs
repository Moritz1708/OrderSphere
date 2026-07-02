using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using OrderSphere.BuildingBlocks.Abstraction;

namespace OrderSphere.BuildingBlocks.Extensions;

/// <summary>
/// Applies the org-level tenant query filter (ADR 0012) to every <see cref="IAuditableEntity"/> in
/// the model, ANDed together with any filter the entity's <c>IEntityTypeConfiguration</c> already
/// set (typically the soft-delete filter, see ADR 0006). EF Core allows only one query filter per
/// entity type, so this combinator must run <em>after</em> <c>ApplyConfigurationsFromAssembly</c>
/// rather than requiring every configuration file to repeat the compound predicate.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Call from <c>DbContext.OnModelCreating</c>, after applying entity configurations, passing
    /// <c>() =&gt; tenantContext.TenantId</c> (a closure over the DbContext's own constructor-injected
    /// <c>ITenantContext</c> field). This must be passed as an <em>expression tree</em>, not a
    /// compiled delegate: EF Core caches the compiled model per context type, so a filter built from
    /// a compiled <c>Func&lt;Guid&gt;</c> would permanently bake in whichever DbContext instance
    /// happened to build the model first. Passed as an expression whose root is a
    /// <see cref="ConstantExpression"/> of the concrete DbContext type (exactly the shape produced by
    /// a lambda referencing an instance field), EF Core's query filter pipeline re-binds that
    /// constant to the actual instance executing each query — the same mechanism documented for
    /// per-context multi-tenancy filters (see EF Core "Global Query Filters — using context data").
    /// </summary>
    public static void ApplyTenantQueryFilter(this ModelBuilder modelBuilder, Expression<Func<Guid>> currentTenantId)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(IAuditableEntity.TenantId));
            Expression tenantCheck = Expression.Equal(tenantProperty, currentTenantId.Body);

            // Pre-existing filters (e.g. the soft-delete filter from ADR 0006) are unnamed, and EF
            // Core only combines *named* filters automatically (EF 10 "named query filters").
            // Fold the unnamed filter's body into this one instead, mirroring the pre-EF10 manual
            // combination pattern, so the single remaining unnamed filter still ANDs both checks.
            var existingFilter = entityType.GetDeclaredQueryFilters()
                .SingleOrDefault(f => f.Key is null)?.Expression;
            if (existingFilter is not null)
            {
                var existingBody = ReplacingExpressionVisitor.Replace(
                    existingFilter.Parameters[0], parameter, existingFilter.Body);
                tenantCheck = Expression.AndAlso(existingBody, tenantCheck);
            }

            entityType.SetQueryFilter(Expression.Lambda(tenantCheck, parameter));
        }
    }
}
