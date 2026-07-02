# Architecture Decision Records

This folder records the significant architectural decisions behind OrderSphere — the context, the
decision, and its consequences — so the reasoning survives independently of the code.

These records were captured **retroactively** from decisions already realized in the codebase; their
status is therefore `Accepted` with the date of documentation. New decisions should be added as a
new numbered file at the time the decision is made.

## Format

Each record uses a lightweight [MADR](https://adr.github.io/madr/)-style structure: **Status**,
**Context**, **Decision**, **Consequences**, and optionally **Alternatives considered** and
**Superseded by**. Keep records short and focused on one decision. Numbering is sequential and
immutable; supersede rather than rewrite.

## Index

| ADR | Title | Status |
|---|---|---|
| [0001](0001-result-type-over-exceptions.md) | `Result<T>` over exceptions for business outcomes | Accepted |
| [0002](0002-customerid-deterministic-guid-from-sub.md) | `CustomerId` as a deterministic v5 GUID derived from `sub` | Accepted |
| [0003](0003-bff-and-api-gateway.md) | BFF + YARP API gateway instead of direct browser-to-service access | Accepted |
| [0004](0004-database-per-service.md) | Database-per-service on PostgreSQL | Accepted |
| [0005](0005-http-clients-and-events-not-grpc.md) | HTTP clients + Service Bus events instead of gRPC | Accepted |
| [0006](0006-soft-delete-global-query-filter.md) | Soft-delete via a global EF query filter | Accepted |
| [0007](0007-event-sourcing-island-order-aggregate.md) | Event-sourcing island: only the Order aggregate | Accepted |
| [0008](0008-saga-choreography-service-bus.md) | Saga choreography over Service Bus for the order/payment flow | Accepted |
| [0009](0009-multi-tenancy-customer-scoping.md) | Single-tenant deployment with per-customer scoping | Superseded by 0012 |
| [0010](0010-shared-infrastructure-building-block.md) | Shared infrastructure building block for Blob storage | Accepted |
| [0011](0011-invoicing-in-api-consumer-no-outbox.md) | Invoicing: in-Api Service Bus consumer without an outbox | Accepted |
| [0012](0012-org-level-multi-tenancy.md) | Org-level multi-tenancy via row-level `TenantId` scoping | Accepted |
