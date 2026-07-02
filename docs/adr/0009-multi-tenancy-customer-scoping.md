# 0009 — Single-tenant deployment with per-customer scoping (not org-level multi-tenancy)

**Status:** Superseded by [0012](0012-org-level-multi-tenancy.md)

## Context

Multi-tenancy can mean different things: separate database instances per organisation, row-level
tenant filtering within a shared schema, or per-user data isolation within a single-tenant
deployment. The term "multi-tenancy" in the roadmap refers to the question of how OrderSphere
isolates one customer's data from another's within the same deployment — not how to serve multiple
organisations from a shared infrastructure.

## Decision

OrderSphere is a **single-tenant deployment** with **per-customer data scoping**. There is no
organisation or tenant concept above the individual customer.

Customer identity is derived once from the Auth0 `sub` claim and expressed as a deterministic
`CustomerId` (see **ADR 0002** for the derivation mechanism). Every service that holds
customer-owned data stores and filters by `CustomerId`; no service-level tenant discriminator
column or query filter exists beyond the customer dimension.

Resource ownership is enforced at the API layer via ABAC (`OrderOwnerOrStaffHandler`
in `Ordering.Api/Authorization/`): the handler derives the requesting user's `CustomerId` from
the JWT and compares it with the resource's stored `CustomerId`. Staff roles bypass the ownership
check and can access any customer's resources.

Cross-service consistency is guaranteed by derivation: `CustomerId.FromSub(sub)` is
deterministic and idempotent, so all services derive the same `CustomerId` from the same `sub`
without a shared registry or foreign-key relationship between services.

## Consequences

- No per-organisation isolation or schema partitioning is implemented. Adding true org-level
  multi-tenancy in the future would require a new ADR and significant schema changes.
- Customer data isolation is a function of the `CustomerId` filter on all relevant queries;
  omitting the filter on a new endpoint is a correctness and data-exposure bug.
- Deterministic `CustomerId` derivation (ADR 0002) is load-bearing for this model: all services
  must use the same derivation algorithm without synchronisation overhead.
- Staff/admin access patterns must be explicitly modelled as role-based bypasses, not
  separate tenants.

## Alternatives considered

- **Org-level multi-tenancy (shared schema with tenant column)** — rejected: no business
  requirement for organisation accounts; adds a discriminator to every table.
- **Org-level multi-tenancy (database-per-org)** — rejected: same reason; disproportionate
  operational complexity.
- **No ownership enforcement (all data accessible to all authenticated users)** — rejected:
  obvious data-isolation failure.

## Superseded by

[0012 — Org-level multi-tenancy via row-level `TenantId` scoping](0012-org-level-multi-tenancy.md)
introduces true organisation-level tenancy once a business requirement for organisation accounts
emerged, using the shared-schema tenant-column approach rejected above as an alternative in 2026-06.
