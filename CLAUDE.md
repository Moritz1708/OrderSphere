# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Claude Code Instructions for OrderSphere

> Architecture reference (project layout, per-service tables, EF migration commands,
> external service wiring) lives in [docs/architecture.md](docs/architecture.md).
> Read it when you need a map of the system; it is not repeated here.

## Operating rules

- Audience for any document or explanation: enterprise architects. No marketing language, no slogans, no enthusiasm filler, no "not just X — it's Y" comparisons.
- Do not guess technical facts. If a concept, API, or library behavior isn't certain, research it before stating it.
- Documents are markdown. Diagrams are mermaid.
- State results and decisions directly.

## Architecture

Microservices over Clean Architecture with CQRS (MediatR) and DDD. Each service is independently deployable and owns its domain, persistence, and infrastructure. Errors flow through `Result<T>`, not exceptions. Entities carry audit fields and soft-delete via `AuditableEntity`.

Layer dependencies point inward toward the domain (canonical Clean Architecture): `Api → Infrastructure → Application → Domain → BuildingBlocks.Domain`, and `Api → Application`. Application defines abstractions (`I<Service>DbContext`, client interfaces); Infrastructure implements them — so `Infrastructure → Application` is correct, not a violation. Application never references Infrastructure. No service references another service's projects directly — cross-service communication uses HTTP clients or Service Bus events. Shared primitives live in `BuildingBlocks` (see [docs/architecture.md](docs/architecture.md)).

## Conventions

These are the rules that are not derivable by reading existing code. For everything else, the existing handlers and configurations are the template — read one before writing a new one.

- Business validation returns a `Result<T>` failure. Exceptions are reserved for genuinely exceptional conditions (I/O failures, programmer errors).
- Commands and queries return `Result<TDto>`. DTOs are `record` types; entities are `class` types.
- New entities inherit `AuditableEntity`. Add a matching EF configuration in the service's `Infrastructure/EntityConfigurations/`.
- Features live in `Features/<Aggregate>/<UseCase>/`, co-locating command/query, handler, and validator. Every service has a `<Service>.Application` project, regardless of size. The sole deliberate exception is the worker-only `Notification` service (documented in [docs/architecture.md](docs/architecture.md)); do not introduce new exceptions. Use-cases go in `<Service>.Application/Features/`. The DbContext is reached through an `I<Service>DbContext` interface declared in `<Service>.Application/Abstractions/` and implemented by the concrete context in `<Service>.Infrastructure/Persistence/`; handlers depend on the interface, never the concrete context.
- Cross-service calls go through typed HTTP client interfaces (e.g. `ICatalogClient`, `IBasketClient`). No direct project references across service boundaries.
- Integration events are defined in `BuildingBlocks.Contracts`. Publish via `IEventBus`; consume via `IIntegrationEventHandler<T>`.
- Soft-delete is enforced by a global query filter, not per-query: every `AuditableEntity` gets `builder.HasQueryFilter(x => !x.IsDeleted)` in its EF configuration, so queries inherit the filter automatically — do not repeat `!x.IsDeleted` in handlers. Use `IgnoreQueryFilters()` only where deleted rows must be read deliberately.
- All I/O is `async`/`await`. No `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`.
- Nullable reference types are enabled. Treat warnings as real.

## UI and styling

All visual, theming, MudBlazor, and CSS rules live in [`docs/ui-conventions.md`](docs/ui-conventions.md). A path-scoped rule in `.claude/rules/ui.md` loads this automatically when `.razor` files are touched.

## Branching

Branch names must follow `feature/{issue-number}-{name}`, `bug/{issue-number}-{name}`, or `refactor/{issue-number}-{name}`, where `{name}` is the kebab-case slug of the linked GitHub sub-issue title. Work with no linked issue (tooling, docs, CI) uses `chore/{name}` — same slug rules, no issue number. Set `ORDERSPHERE_SKIP_BRANCH_CHECK=1` to bypass the check for anything else. Enforced locally via Git hooks (`.githooks/`), auto-installed by a `Directory.Build.props` build target. Full details, including the GitHub Projects status automation tied to this schema, are in [docs/branch-naming-convention.md](docs/branch-naming-convention.md).

## Navigation discipline

Answer "who calls X" or "where is Y implemented" without loading whole files into context.

- **If the `cwm-roslyn-navigator` MCP tools are available** (`find_references`, `find_implementations`, `get_type_hierarchy`, `find_callers`), use them first — they answer symbol questions directly. They are not part of this repository's toolchain: they ship with the third-party `dotnet-claude-kit` plugin, which each developer installs (or does not) individually.
- **Otherwise** fall back to `Grep`/`Glob` to locate the symbol, then read only the files the search points at.

For broad codebase investigations (spanning more than 3–4 files), use an Explore subagent so the read overhead stays in the subagent's context, not the main session. For multi-step tasks requiring planning, use Plan mode before implementation.

## Commands

Run from the repository root. Full EF migration matrix is in [docs/architecture.md](docs/architecture.md).

| Task | Command |
|---|---|
| Build | `dotnet build OrderSphere.slnx` |
| Run via Aspire | `dotnet run --project src/Hosting/OrderSphere.AppHost` |
| Run BFF (with WASM) | `dotnet run --project src/Gateways/OrderSphere.Bff` |
| All tests | `dotnet test` |
| One test project | `dotnet test tests/OrderSphere.Domain.Tests` |
| Single test / by name | `dotnet test --filter "FullyQualifiedName~CheckoutCart"` (also accepts `Name~`, `ClassName~`) |

## Ask before

- Adding a NuGet dependency.
- Schema changes that aren't trivially backward-compatible.
- Introducing a new architectural pattern or cross-cutting concern.
- Changing authentication or authorization flow.
- Breaking a public contract consumed by the UI client.

Proceed without asking for: bug fixes, refactors inside one layer, new features that follow an existing feature pattern, UI changes consistent with `docs/ui-conventions.md`, performance improvements with no behavior change.

## Commit format

Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`, `style:`, `test:`, `chore:`. One-line subject describing the user-visible change; body for rationale if non-obvious.
