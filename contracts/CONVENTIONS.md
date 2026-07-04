# Service Contract Conventions

How services expose and consume contracts in OrderSphere. Two contract surfaces exist today:
**synchronous** typed HTTP clients and **asynchronous** integration events over Azure Service Bus.
This document is the canonical reference for both; the behavioural rules it depends on live in
[../CLAUDE.md](../CLAUDE.md) and the system map in [../docs/architecture.md](../docs/architecture.md).

> **Scope note.** Typed HTTP clients are the default for synchronous cross-service calls. gRPC is a
> deliberate, scoped exception used for exactly one path today (Basket → Catalog); see
> [Synchronous contracts — gRPC (exception case)](#synchronous-contracts--grpc-exception-case).
> Per-service OpenAPI/NuGet contract packages are **not** implemented; the `contracts/openapi/`
> folder is a placeholder — see [Not implemented](#not-implemented--out-of-scope) at the end. Do not
> treat that placeholder as an active convention.

## Synchronous contracts — typed HTTP clients

Cross-service calls go through a typed client **interface** declared in the consuming service's
`Application/Abstractions/` and implemented in its `Infrastructure/` against the producer's REST API.
No service references another service's projects; the only coupling is the HTTP surface.

- Interface naming: `I<Producer>Client` (e.g. `ICatalogClient`, `IBasketClient`).
  Representative paths:
  - `src/Services/Ordering/OrderSphere.Ordering.Application/Abstractions/ICatalogClient.cs`
  - `src/Services/Basket/OrderSphere.Basket.Application/Abstractions/ICatalogClient.cs`
- Methods return `Result<T>` (or `Result`), never throw for business failures — consistent with the
  `Result<T>` contract in [../CLAUDE.md](../CLAUDE.md).
- Request/response payloads are `record` DTOs co-located with the interface
  (e.g. `CatalogProductInfo`). They are owned by the **consumer** and shaped to its needs; they are
  not shared types.
- The Blazor client has its own typed clients under `src/Frontend/OrderSphere.Web/Services/`
  (`CatalogClient`, `OrderingClient`, …) that call the BFF/Gateway, not the services directly.

### REST / versioning

- Endpoints are versioned by URL segment: `MapGroup("api/v{version:apiVersion}/<resource>")`
  (`Asp.Versioning`), so the public surface is `/api/v1/...`. `v1` is the current stable major.
- A breaking change introduces `/api/v2/` alongside `v1`; backward-compatible additions stay in the
  existing version.
- Routes are exposed to external callers only through the YARP API Gateway
  (`src/Gateways/OrderSphere.ApiGateway`); services are not reached directly from the browser.

## Synchronous contracts — gRPC (exception case)

HTTP clients remain the default for new cross-service calls. One internal path uses gRPC instead,
as a deliberate, scoped choice for a latency-sensitive hot path — not a second general-purpose
convention:

- **Basket → Catalog** stock checks (`GetProductByIdAsync` / `GetProductInfosByIdsAsync`) go over
  gRPC. Proto contract: `contracts/proto/catalog/v1/catalog.proto` (`CatalogService`).
  - Server: `CatalogGrpcService` in
    `src/Services/Catalog/OrderSphere.Catalog.Api/Grpc/CatalogGrpcService.cs`, registered via
    `AddGrpc()` and `MapGrpcService<CatalogGrpcService>().RequireAuthorization()`.
  - Client: `GrpcCatalogClient` in
    `src/Services/Basket/OrderSphere.Basket.Infrastructure/CatalogClient/GrpcCatalogClient.cs`,
    which implements `ICatalogClient` — the same interface an HTTP-backed client would implement,
    so callers stay transport-agnostic. Registered via
    `AddGrpcClient<CatalogService.CatalogServiceClient>(...).AddClientCredentialsHandler()`.
  - M2M authentication reuses the same client-credentials handler as HTTP clients; there is no
    separate auth mechanism for gRPC.
- Do not default to gRPC for new cross-service calls without the "introducing a new architectural
  pattern" sign-off in [../CLAUDE.md](../CLAUDE.md) — this exception is scoped to the one path
  above.

## Asynchronous contracts — integration events

Cross-service events are JSON messages on Azure Service Bus queues, defined as shared DTOs so
producer and consumer agree on the schema.

- Event records live in `src/BuildingBlocks/OrderSphere.BuildingBlocks.Contracts/Events/` and derive
  from `IntegrationEvent` (`BuildingBlocks.EventBus`), which carries `Id`, `CreatedAt`, and
  `CorrelationId`. Current events: `CheckoutCartIntegrationEvent`, `OrderPlacedIntegrationEvent`,
  `OrderStatusChangedIntegrationEvent`, `PaymentRequestedIntegrationEvent`,
  `PaymentProcessedIntegrationEvent`, `RealtimeNotificationEvent`, `StockReservedIntegrationEvent`,
  `InvoiceGeneratedIntegrationEvent`.
- Naming: `<Subject><PastTense>IntegrationEvent` (e.g. `OrderPlacedIntegrationEvent`). Records are
  `sealed`; properties are `required` / `init`-only. Nested payload DTOs use the `…Dto` suffix
  (e.g. `OrderPlacedItemDto`).
- Publish via `IEventBus.PublishAsync(@event, destination)` where `destination` is the target queue
  name; producers publish through the transactional outbox (see the outbox handlers under each
  service's `Infrastructure/Outbox/`). Consumers are background workers that read one queue each.
- The queue inventory (producer → consumer per queue) is documented in
  [../docs/architecture.md](../docs/architecture.md#external-services); queue names are declared in
  `src/Hosting/OrderSphere.AppHost/AppHost.cs`.

### Schema-compatibility rules

- Backward-compatible change (new optional property): keep the same event record.
- Breaking change (remove/rename/retype a property, change semantics): introduce a new event type
  rather than mutating the existing one, and migrate consumers before retiring the old event.
- Because `BuildingBlocks.Contracts` is referenced by every producer and consumer, treat any change
  to an event record as a public-contract change — covered by the "Ask before … breaking a public
  contract" rule in [../CLAUDE.md](../CLAUDE.md).

## Not implemented / out of scope

The following are **not** part of the current system. They are recorded here only so the empty
placeholder folder is not mistaken for an active convention:

- **Per-service OpenAPI spec files** — `contracts/openapi/` is empty. API descriptions are generated
  at runtime from the minimal-API endpoints, not maintained as committed spec files.
- **Published NuGet contract packages** (`OrderSphere.Contracts.<Service>.V1`) — do not exist. Shared
  event DTOs live in the in-repo `BuildingBlocks.Contracts` project; HTTP DTOs are consumer-owned.

Adopting any of these is an architectural change and requires sign-off per
[../CLAUDE.md](../CLAUDE.md) ("Ask before … introducing a new architectural pattern").
