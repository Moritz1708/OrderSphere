# OrderSphere Documentation

Entry point for the `docs/` tree. The repository-root [README.md](../README.md) is the project
overview; the documents below are the detail it points to. Behavioural rules and conventions live in
[../CLAUDE.md](../CLAUDE.md).

## Reference

| Document | Purpose |
|---|---|
| [architecture.md](architecture.md) | System map: project layout, per-service tables, feature inventory, AI advisory/MCP design, Service Bus queues, EF migration matrix, external services. |
| [glossary.md](glossary.md) | Ubiquitous language — aggregates and core domain terms. |
| [auth/role-model.md](auth/role-model.md) | Roles, ASP.NET authorization policies, ABAC handlers, MFA, token claims, and `sub`-derived identity. |
| [ui-conventions.md](ui-conventions.md) | Binding visual/theming/MudBlazor/CSS reference for the Blazor client — the "Bold Editorial" design system (see [ADR 0013](adr/0013-editorial-design-system.md)). |
| [../contracts/CONVENTIONS.md](../contracts/CONVENTIONS.md) | Service contract conventions: typed HTTP clients and integration-event schemas. |

## Decisions

| Document | Purpose |
|---|---|
| [adr/README.md](adr/README.md) | Architecture Decision Records — index and individual decisions with context and consequences. |
| [assessments/README.md](assessments/README.md) | Dated, immutable maturity snapshots of the project over time. |

## Operations and deployment

| Document | Purpose |
|---|---|
| [deploy-ordersphere.md](deploy-ordersphere.md) | Azure deployment via `azd`: prerequisites, parameters, secrets, step-by-step, troubleshooting. |
| [operations.md](operations.md) | Observability and operations runbook: traces, health checks, Aspire dashboard, resilience. |
| [github/branching.md](github/branching.md) | Branching strategy and workflow. |
| [github/branch-protection.md](github/branch-protection.md) | Branch-protection ruleset and required status checks for `master`. |

## Security and process

| Document | Purpose |
|---|---|
| [../SECURITY.md](../SECURITY.md) | Security policy, vulnerability reporting, automated controls, trust boundaries. |
| [../CONTRIBUTING.md](../CONTRIBUTING.md) | Contribution workflow and local checks. |
| [../CHANGELOG.md](../CHANGELOG.md) | Notable changes (Keep a Changelog format). |
