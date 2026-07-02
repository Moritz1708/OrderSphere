# 0012 — Org-level multi-tenancy via row-level `TenantId` scoping

**Status:** Accepted — supersedes [0009](0009-multi-tenancy-customer-scoping.md)

## Context

[ADR 0009](0009-multi-tenancy-customer-scoping.md) established a single-tenant deployment with
per-customer data scoping: no organisation concept existed above the individual customer, and
`CustomerId` (deterministic from the Auth0 `sub` claim, see [0002](0002-customerid-deterministic-guid-from-sub.md))
was the sole isolation dimension.

Workstream B item B5 requires true org-level multi-tenancy: multiple organisations ("tenants")
share the same OrderSphere deployment, each with its own customers, orders, catalog activity, and
configuration, fully isolated from one another. This is a materially different model from ADR 0009
and requires a new decision, as 0009 anticipated ("Adding true org-level multi-tenancy in the future
would require a new ADR").

## Decision

OrderSphere adopts **row-level multi-tenancy on a shared schema**: every service keeps a single
database (per [0004](0004-database-per-service.md)), and every tenant-owned row carries a
`TenantId` (`Guid`) discriminator column, enforced by a global EF Core query filter — the same
mechanism already used for soft-delete ([0006](0006-soft-delete-global-query-filter.md)).

- **Placement:** `TenantId` is added to `IAuditableEntity` / `AuditableEntity<TId>` in
  `BuildingBlocks.Domain`, so every auditable entity across every service is tenant-scoped
  uniformly. There is no per-entity opt-out — a service with no organisation concept (e.g. a
  future single-tenant deployment) simply resolves one default tenant everywhere.
- **Resolution source:** `TenantId` is resolved from the Auth0 Organizations `org_id` claim,
  deterministically mapped to a `Guid` the same way `CustomerId.FromSub` derives from `sub`
  (RFC 4122 v5 GUID over the claim value) — consistent with the "derivation, not a shared registry"
  principle from ADR 0002. No cross-service tenant lookup is required for isolation to hold.
- **Enforcement:** a single generic model-builder extension in `BuildingBlocks.Domain` combines the
  existing per-entity soft-delete filter with a `TenantId` equality check via expression-tree
  composition, applied once per `DbContext.OnModelCreating` after `ApplyConfigurationsFromAssembly`.
  This avoids repeating `&& x.TenantId == currentTenant` in all ~19 existing `IEntityTypeConfiguration`
  classes across every service.
- **Stamping:** `ChangeTrackerExtensions.ApplyAuditFields` is extended to also stamp `TenantId` on
  `Added` entities from the ambient tenant context, mirroring how `CreatedAt`/`IsDeleted` are
  already stamped centrally rather than per-handler.
- **API vs. worker context:** `ITenantContext` (`BuildingBlocks.Domain/Security`) is the
  cross-cutting abstraction. In API processes it is backed by `HttpContextTenantContext` (analogous
  to `HttpContextCurrentUser`), reading the resolved claim per request. In worker/background
  processes — which have no `HttpContext` — it is backed by an `AsyncLocal`-based ambient context
  that each message-consuming loop must set explicitly from a `TenantId` field carried on the
  integration event before invoking any persistence code, then clear afterwards.
- **Ownership vs. tenancy:** the existing ABAC ownership check (`OrderOwnerOrStaffHandler`,
  `CustomerId`-based) is unchanged and continues to operate *within* a tenant. Tenant isolation is
  enforced one level above customer ownership, by the query filter, not by the ABAC handler.
- **Tenant management:** tenant onboarding and per-tenant configuration live in the UserProfile
  service as a new `Tenant` aggregate — the closest existing identity-adjacent bounded context —
  exposed via admin-only use-cases. Other services never join against this table; they only compare
  the scalar `TenantId` derived from the claim, consistent with the no-cross-service-FK precedent
  set by `CustomerId`.

## Consequences

- Every service's `DbContext` gains a constructor dependency on `ITenantContext` and every
  `SaveChangesAsync` override continues to route through the centralized `ApplyAuditFields` helper —
  no service-specific stamping logic.
- Every service's schema gains a `TenantId` column (see per-service `AddTenantId` migrations).
  Existing rows are backfilled to a single default tenant GUID so the migration is backward
  compatible with the pre-multi-tenant, single-organisation deployment.
- Integration events that a worker needs to persist against now carry `TenantId` as an additive,
  nullable field; omitting it is a correctness bug for tenant-scoped data, mirrored by the same risk
  ADR 0009 already called out for omitting the `CustomerId` filter.
- EF Core supports only one query filter per entity type; the combinator extension must correctly
  AND the pre-existing filter rather than replace it, or soft-deleted rows would leak across the
  tenant boundary or vice versa. This is centralized in one tested helper rather than duplicated
  per configuration file.
- A tenant compromise or misconfigured `org_id` claim is a full data-isolation failure across every
  service simultaneously (row-level model has no physical separation) — same risk class ADR 0009
  already accepted for the customer dimension, now one level higher.

## Alternatives considered

- **Database-per-tenant** — rejected: breaks [0004](0004-database-per-service.md) two ways at once
  (already database-per-service; multiplying by tenant count is operationally disproportionate for
  the current scale) and requires dynamic connection-string routing infrastructure that does not
  otherwise exist in this codebase.
- **Schema-per-tenant within one database** — rejected: EF Core migrations and the existing
  `ApplyConfigurationsFromAssembly` model would need per-tenant schema switching; no proportional
  isolation benefit over a row-level filter at this scale.
- **Editing all ~19 `IEntityTypeConfiguration` classes to add the compound filter individually** —
  rejected: mechanical, error-prone repetition across every service for a rule that is identical
  everywhere; a single shared combinator is the DRY equivalent of the existing single-predicate
  soft-delete filter.
