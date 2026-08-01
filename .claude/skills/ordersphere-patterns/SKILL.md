---
name: ordersphere-patterns
description: OrderSphere's non-obvious code conventions — Result<T> error flow, AuditableEntity + soft-delete query filter, CQRS feature layout, I<Service>DbContext indirection, Outbox/Inbox eventing, and MCP bearer-forwarding. Read before adding a handler, entity, integration event, or MCP tool.
---

# OrderSphere code patterns

Authoritative conventions for this repository, derived from the existing code. Use these
as the template when writing new code. For the system map (services, layering, EF migration
commands), read `docs/architecture.md`; for the operating rules, read `CLAUDE.md`. This skill
covers the conventions that are **not obvious from a quick read** and are easy to get wrong.

Audience: enterprise architects. State decisions directly; no marketing language.

## When to use

Invoke before:
- Adding a command/query handler.
- Adding a new entity or EF configuration.
- Defining or consuming an integration event.
- Adding an MCP tool to the advisory agent.
- Reviewing a change for convention drift.

## 1. Errors flow through `Result<T>`, not exceptions

Business outcomes (not found, invalid, conflict) return a `Result<T>` failure. Exceptions are
reserved for genuinely exceptional conditions (I/O failure, programmer error) and are caught,
logged, and converted to a failure at the handler boundary.

- Commands and queries return `Result<TDto>`. DTOs are `record`; entities are `class`.
- Errors are predefined static `Error` values per aggregate, e.g.
  `src/Services/Ordering/OrderSphere.Ordering.Domain/Errors/OrderErrors.cs`.

Template: `src/Services/Ordering/OrderSphere.Ordering.Application/Features/Order/GetOrderByIdQueryHandler.cs`
```csharp
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<Result<OrderDto>>;

public sealed class GetOrderByIdQueryHandler(
    IOrderingDbContext context,
    ILogger<GetOrderByIdQueryHandler> logger
) : IQueryHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        try
        {
            var order = await context.Orders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == OrderId.From(request.OrderId), ct);
            if (order is null)
                return Result<OrderDto>.Failure(OrderErrors.OrderNotFoundError);
            return Result<OrderDto>.Success(/* map */);
        }
        catch (OperationCanceledException) { throw; }     // never swallow cancellation
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving order {OrderId}", request.OrderId);
            return Result<OrderDto>.Failure(OrderErrors.UnknownError);
        }
    }
}
```
Note: `OperationCanceledException` is rethrown, never converted to a failure.

## 2. Feature layout (CQRS via MediatR)

Use-cases live in `<Service>.Application/Features/<Aggregate>/<UseCase>/`, co-locating the
command/query, its handler, and its FluentValidation validator. Admin use-cases nest under
`Features/<Aggregate>/Admin/`. Example with full set:
`src/Services/Ordering/OrderSphere.Ordering.Application/Features/Coupon/Admin/CreateCoupon/`.

Marker interfaces: `ICommand<T>`/`IQuery<T>` and `ICommandHandler`/`IQueryHandler` from
`OrderSphere.BuildingBlocks.Abstraction`. Validation runs as a MediatR pipeline behavior
(`BuildingBlocks.Domain/Behaviors/ValidationBehavior.cs`) — do not call validators manually.

## 3. DbContext is reached through `I<Service>DbContext`

Handlers depend on the **interface** in `<Service>.Application/Abstractions/`
(e.g. `IOrderingDbContext`), never the concrete context. The concrete context lives in
`<Service>.Infrastructure/Persistence/` and implements the interface. This keeps Application
free of an Infrastructure reference.

## 4. Entities inherit `AuditableEntity<TId>`; soft-delete is a query filter

`src/BuildingBlocks/OrderSphere.BuildingBlocks.Domain/Abstraction/AuditableEntity.cs` provides
`Id` (strongly-typed), `CreatedAt`, `UpdatedAt`, `IsDeleted`, and domain-event collection.

Soft-delete is enforced **once per entity** in its EF configuration — never repeated in
handlers. Template: `src/Services/Catalog/OrderSphere.Catalog.Infrastructure/EntityConfigurations/BrandConfiguration.cs`
```csharp
builder.ToTable("brands");
builder.HasKey(b => b.Id);
builder.HasQueryFilter(b => !b.IsDeleted);   // global soft-delete filter
```
Consequences:
- Do **not** write `!x.IsDeleted` in queries — the filter applies automatically.
- Use `IgnoreQueryFilters()` only where deleted rows must be read deliberately.
- Every new `AuditableEntity` needs a matching `IEntityTypeConfiguration<T>` in
  `<Service>.Infrastructure/EntityConfigurations/` plus an EF migration.

Strongly-typed IDs (e.g. `OrderId`, `ProductId`) live in
`BuildingBlocks.Domain/StronglyTypedIds/` and map to UUID columns via `ConfigureConventions`.
Convert at service boundaries with `OrderId.From(guid)` / `.Value`.

## 5. Integration events: Outbox to publish, Inbox to consume

Contracts are records in `BuildingBlocks.Contracts/Events/` deriving from `IntegrationEvent`
(e.g. `OrderStatusChangedIntegrationEvent`). Naming/versioning rules: `contracts/CONVENTIONS.md`.

- **Publish** transactionally via the Outbox: write an `OutboxMessage`
  (`BuildingBlocks.EventBus/Outbox/OutboxMessage.cs`, UUIDv7 id, `MaxRetries = 10`,
  `TraceParent` captured for trace continuity) in the same DbContext transaction as the state
  change. The dispatcher (`BuildingBlocks.EventBus.AzureServiceBus/Outbox/OutboxDispatcher.cs`)
  publishes asynchronously. Outbox event handlers live in
  `<Service>.Infrastructure/Outbox/` and implement `IOutboxEventHandler`.
- **Consume** idempotently via the Inbox (`BuildingBlocks.EventBus/Inbox/`,
  `EfInboxStore`) — consumers dedupe redelivered messages (Service Bus is at-least-once).
- Never reference another service's projects. Cross-service sync calls use typed HTTP client
  interfaces (`ICatalogClient`, `IBasketClient`).

## 6. MCP advisory tools are read-only and user-scoped

MCP tools live in `src/Services/Advisory/OrderSphere.Mcp.Server/Tools/`. Every existing tool is
`[McpServerTool(ReadOnly = true, Destructive = false)]` and calls the API Gateway through
`IOrderSphereGateway`, with the caller's bearer token forwarded by `BearerForwardingHandler`
(so the downstream service derives the customer from the JWT). User-scoped tools guard with
`UserToolGuard.AuthRequired` when no token is present. The advisory agent builds its tool set
per request (`Advisory.Api/Agent/AdvisorToolSource.cs`) so tools bind to the current user.

A non-read-only tool is a deliberate exception (see roadmap item C1) and must use an explicit
human-in-the-loop confirmation — do not add a silent write tool.

## 7. Async, nullable, and "ask before"

- All I/O is `async`/`await`. No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`.
- Nullable reference types are on; treat warnings as real.
- Per `CLAUDE.md`, ask before: new NuGet dependency, non-trivial schema change, new
  architectural pattern/cross-cutting concern, auth/authz changes, breaking a UI-consumed contract.

## Verification

After changes: `dotnet build OrderSphere.slnx`, then `dotnet test` (the CI line-coverage gate
`MIN_LINE` in `.github/workflows/ci.yml` must hold — new logic needs accompanying tests; see
`docs/test-coverage-plan.md`). For a single feature: `dotnet test --filter "Name~<UseCase>"`.
