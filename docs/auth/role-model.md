# Role Model

Roles are defined in the Auth0 tenant (Authorization Core / RBAC) and assigned to users there. A
Post-Login Action emits the user's effective roles into the namespaced access-token claim
`https://ordersphere.dev/roles`; each service maps that claim to the ASP.NET role claim type
(`Oidc:RolesClaim`, default `https://ordersphere.dev/roles`), so `User.IsInRole(...)` reads it
directly. This document maps those roles to ASP.NET authorization policies and ABAC handlers.

## Roles

| Role | Type | Assigned to | Effective permissions |
|---|---|---|---|
| `customer` | Simple | Every end-user (assigned on first login) | Own cart, own orders, own profile |
| `csr` | Composite (`requires-mfa`) | Customer Service staff | Read all orders and customer data; no writes |
| `order-manager` | Composite (`requires-mfa`) | Operations staff | Update order status, cancel orders |
| `catalog-admin` | Composite (`requires-mfa`) | Merchandising staff | Full CRUD on catalog products and categories |
| `admin` | Composite (`csr` + `order-manager` + `catalog-admin` + `requires-mfa`) | Platform administrators | All of the above |
| `requires-mfa` | Simple (gating only) | Inherited via `csr`, `order-manager`, `catalog-admin`, `admin` | Triggers MFA second factor in browser flow |
| `svc.ordering` | Simple (machine) | `ordering-worker` service account | Machine identity for Catalog M2M calls |
| `svc.notification` | Simple (machine) | `notification-worker` service account | Machine identity for future UserProfile M2M calls |

### Composite Role Inheritance

The effective model is hierarchical:

```
admin
├── csr         ──► requires-mfa
├── order-manager ► requires-mfa
├── catalog-admin ► requires-mfa
└── requires-mfa (direct)
```

Auth0 has no native composite-role expansion. The hierarchy is realized in the Post-Login Action:
when it emits the `https://ordersphere.dev/roles` claim it flattens the tree, so a user holding
`admin` receives all four child roles (and `requires-mfa`) in the claim. MFA is triggered for any
user who effectively holds `requires-mfa` (see [MFA](#mfa)).

---

## ASP.NET Authorization Policies

### Ordering.Api (`src/Services/Ordering/OrderSphere.Ordering.Api/`)

| Policy constant | Definition | File |
|---|---|---|
| `AuthorizationPolicies.Admin` | `RequireRole("admin")` | `Configuration/AuthorizationExtensions.cs` |
| `AuthorizationPolicies.Staff` | `RequireRole("csr", "order-manager", "admin")` | same |
| `AuthorizationPolicies.OrderManager` | `RequireRole("order-manager", "admin")` | same |
| `AuthorizationPolicies.OrderOwnerOrStaff` | Resource-based ABAC | same |

### Catalog.Api (`src/Services/Catalog/OrderSphere.Catalog.Api/`)

| Policy constant | Definition | File |
|---|---|---|
| `AuthorizationPolicies.CatalogAdmin` | `RequireRole("catalog-admin", "admin")` | `Configuration/AuthorizationExtensions.cs` |

### UserProfile.Api (`src/Services/UserProfile/OrderSphere.UserProfile.Api/`)

| Policy constant | Definition | File |
|---|---|---|
| `AuthorizationPolicies.Admin` | `RequireRole("admin")` | `Configuration/AuthorizationExtensions.cs` |

### BFF (`src/Gateways/OrderSphere.Bff/`)

| Policy | Definition | Applied to |
|---|---|---|
| `BffUserPolicy` | `RequireAuthenticatedUser()` | All `/api/**` reverse-proxy routes, except the anonymous catalog read routes (`GET`/`HEAD` on `/api/v1/products`, `/categories`, `/brands`, `/reviews`) declared with `AuthorizationPolicy: anonymous` in `appsettings*.json`. Writes on those paths fall through to the catch-all and stay authenticated |

---

## ABAC Handlers

Resource-based access control is implemented as `IAuthorizationHandler<TRequirement, TResource>` instances.

### `OrderOwnerOrStaffHandler`

**File**: `src/Services/Ordering/OrderSphere.Ordering.Api/Authorization/OrderOwnerOrStaffHandler.cs`

**Logic**:
1. If `User.IsInRole("csr") || User.IsInRole("order-manager") || User.IsInRole("admin")` → **pass** (staff).
2. Else if `CustomerId.FromSub(User.FindFirst("sub").Value).Value == order.CustomerId` → **pass** (owner).
3. Otherwise → **does not succeed** → framework denies (403).

**Usage pattern** in endpoint handlers:
```csharp
var authResult = await authorizationService.AuthorizeAsync(
    User, order, AuthorizationPolicies.OrderOwnerOrStaff);
if (!authResult.Succeeded)
    return Results.Forbid();
```

---

## MFA

Staff users (those who effectively hold `requires-mfa` via the role hierarchy) must complete a
second factor. In Auth0 this is enforced by a Post-Login Action that requests MFA conditionally:

```
Post-Login Action
  if user roles include 'requires-mfa'  ──► api.multifactor.enable('any', { allowRememberBrowser: true })
  else                                  ──► no second factor
```

Permitted factors (WebAuthn, TOTP/OTP) are configured under the tenant's Multi-factor Authentication
settings. Regular `customer` users complete login after the first factor only, because the Action
does not request MFA for them.

---

## Token Claims

The namespaced claim `https://ordersphere.dev/roles` in issued access tokens contains the user's
effective roles (flattened hierarchy, emitted by the Post-Login Action). Services configure
`RoleClaimType` to that claim name (`OidcAuthenticationExtensions`, `Oidc:RolesClaim`), so
`User.IsInRole(...)` reads from it directly. The BFF additionally re-emits the namespaced claim as
`roles` on the cookie principal so `/bff/user` and the WASM client read roles uniformly.

Example access token payload:
```json
{
  "sub": "auth0|e5f6a7b8...",
  "email": "moritz.waldau@ordersphere.dev",
  "https://ordersphere.dev/roles": ["admin", "customer", "csr", "order-manager", "catalog-admin", "requires-mfa"],
  "aud": "https://api.ordersphere.dev",
  "iss": "https://ordersphere-dev.eu.auth0.com/",
  "exp": 1748000000
}
```

---

## Identity derivation (`sub`)

The Auth0 `sub` claim (format `auth0|<opaque_id>`) is the single source of caller identity. Two
representations are derived from it, by design, and never joined directly across service
boundaries:

| Representation | Where | Derivation |
|---|---|---|
| `CustomerProfile.Subject` (raw string) | UserProfile service | Stored verbatim from `sub`; unique index. |
| `CustomerId` (GUID) | Ordering, Basket, Webhooks | `CustomerId.FromSub(sub)` — deterministic RFC 4122 v5 GUID (SHA-256 of `sub`). |

Both originate from the same `sub`, so a given user maps to exactly one `Subject` string and one
`CustomerId` GUID. The GUID form lets order/cart/subscription aggregates use a strongly-typed,
fixed-width key without storing the opaque Auth0 identifier, while UserProfile keeps the raw `sub`
as the lookup key for profile read/write. There is no foreign key between the two; consistency rests
solely on `FromSub` being deterministic.
