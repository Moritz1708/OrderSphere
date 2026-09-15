# 0003 — BFF + YARP API gateway instead of direct browser-to-service access

**Status:** Accepted

## Context

The frontend is a Blazor WebAssembly client. A public SPA cannot safely hold OIDC tokens: tokens in
the browser are exposed to XSS and require client-side refresh handling. The system also has many
services that should not each be a public ingress with its own CORS and auth surface.

## Decision

The browser never talks to services directly. A Backend-for-Frontend (`OrderSphere.Bff`) hosts the
WASM client, owns the OIDC code flow, and keeps the session in an encrypted HTTP-only cookie (no
tokens in the browser). All API traffic is proxied: BFF → YARP API gateway
(`OrderSphere.ApiGateway`) → services. The gateway is the single external API ingress; services
validate the forwarded JWT and enforce per-service RBAC.

**Anonymous catalog reads (2026-09).** Browsing is public: `GET`/`HEAD` on `/api/v1/products`,
`/categories`, `/brands` and `/reviews` are declared anonymous on both hops (BFF routes ordered before
the authenticated catch-all, gateway routes method-restricted so the stock and review `POST`s stay
authenticated), and the Catalog service itself has always left its public read groups open. An
anonymous request resolves to the default tenant (`TenantId == Guid.Empty`, see ADR 0012); the
product-by-slug cache key is tenant-scoped so an anonymous miss cannot populate an organisation's entry.

See [../auth/role-model.md](../auth/role-model.md) for the policy/claims model and
[../architecture.md](../architecture.md) for the request flow.

## Consequences

- Tokens stay server-side; the browser holds only a cookie. CSRF protection is applied at the BFF.
- One public ingress (plus the deliberately external MCP server); services are not internet-facing.
- Cross-cutting concerns (session, CSRF, fan-out) live in one place rather than in every service.
- The BFF and gateway are on the critical path for every request and must be available and observable.
- Real-time updates require a server-side channel (SignalR backplane on the BFF), since the browser
  has no direct service connection.

## Alternatives considered

- **Public SPA holding tokens** — rejected: token exposure, refresh complexity, per-service CORS/auth.
- **API gateway without a BFF** — rejected: still pushes token custody and session to the browser.
