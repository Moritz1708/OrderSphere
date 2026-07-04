# 0005 — HTTP clients + Service Bus events instead of gRPC

**Status:** Accepted (amended 2026-07-04)

## Context

Services must communicate without referencing each other's projects. Two interaction styles are
needed: synchronous request/response (e.g. Basket validating stock against Catalog) and asynchronous,
decoupled propagation (e.g. order placed → notification, payment, webhooks). gRPC/Protobuf was an
option for the synchronous path and was scaffolded as a contracts folder. It was later adopted for
one specific internal path — see [Amendment](#amendment-2026-07-04) below.

## Decision

- **Synchronous:** typed HTTP client interfaces (`I<Producer>Client`) declared in the consumer's
  `Application/Abstractions/` and implemented in `Infrastructure/` against the producer's REST API.
  Requests/responses are consumer-owned `record` DTOs; methods return `Result<T>`.
- **Asynchronous:** integration events as JSON on Azure Service Bus queues, defined as shared records
  in `BuildingBlocks.Contracts` and published through a transactional outbox via `IEventBus`.
- **gRPC is not the default transport; published OpenAPI/NuGet contract packages are out of
  scope.** One gRPC exception exists (Basket → Catalog, see [Amendment](#amendment-2026-07-04));
  the `contracts/openapi/` folder remains a placeholder.

See [../../contracts/CONVENTIONS.md](../../contracts/CONVENTIONS.md) for the full conventions.

## Consequences

- One transport (HTTP/JSON) and one messaging mechanism (Service Bus) to operate, debug, and observe
  — no Protobuf toolchain or codegen pipeline.
- Contracts are plain C# (HTTP DTOs and event records); changes to event records are public-contract
  changes requiring sign-off.
- No cross-language strong typing or streaming benefits that gRPC would provide; acceptable for an
  all-.NET system.
- Adopting gRPC later is a deliberate architectural change, not a drop-in.

## Alternatives considered

- **gRPC for synchronous calls** — rejected as the default: extra toolchain and codegen for an
  all-.NET estate; HTTP clients suffice for most calls. Later adopted narrowly for one
  latency-sensitive path rather than reversing the general decision (see Amendment below).
- **Published NuGet contract packages per service** — rejected: in-repo `BuildingBlocks.Contracts`
  plus consumer-owned HTTP DTOs avoid versioned-package overhead.

## Amendment (2026-07-04)

Basket → Catalog stock checks (`GetProductByIdAsync` / `GetProductInfosByIdsAsync`) now use gRPC
instead of an HTTP client: server `CatalogGrpcService` in `OrderSphere.Catalog.Api`, client
`GrpcCatalogClient` (implements `ICatalogClient`) in `OrderSphere.Basket.Infrastructure`, proto
contract at `contracts/proto/catalog/v1/catalog.proto`. This is a scoped exception for one
latency-sensitive hot path, not a reversal of the decision above — HTTP clients remain the default
for new cross-service calls. See
[../../contracts/CONVENTIONS.md](../../contracts/CONVENTIONS.md#synchronous-contracts--grpc-exception-case)
for the current convention.
