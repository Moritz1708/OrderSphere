# OrderSphere — Azure deployment (azd)

Deploying OrderSphere into a dedicated Azure DEV environment via the Azure Developer CLI
(`azd`). OrderSphere is a .NET Aspire application: the AppHost manifest
(`src/Hosting/OrderSphere.AppHost`) is the single source of the resource topology. `azd` reads the
manifest and generates the Bicep templates from it (Container Apps, Service Bus, Azure Managed
Redis, Key Vault). There is deliberately **no** hand-maintained `infra/` folder.

Postgres deploys as a container inside Container Apps by default, in every azd environment —
some subscriptions (including the one behind `ordersphere-dev`) reject Azure PostgreSQL Flexible
Server. An environment whose subscription does support Flexible Server can opt in explicitly via
`azd env set Deployment__UseManagedPostgres true` before its first `azd up`/`azd provision`; see
[deploy-ordersphere-budget.md](deploy-ordersphere-budget.md) for a deployment that does this.

Auth0 is **not** part of this deployment. It is an external, managed identity provider (tenant
`ordersphere-dev.eu.auth0.com`). The coupling is solely through the issuer URL (`oidc-authority`
parameter) and four confidential client secrets. The Auth0-side configuration (applications, API,
M2M grants, roles action) is described in [auth/role-model.md](auth/role-model.md) and reconciled
against the deployed BFF URL in step 4 below.

## Prerequisites

- `azd` installed (`azd version`), `dotnet` 10 SDK.
- An Azure subscription with permission to create the resource group `rg-dev`.
- An Auth0 tenant with the OrderSphere applications and API configured (issuer URL known).
- Docker (for the local container build performed by `azd`).

## Key facts

| Key | Value |
|---|---|
| Environment | `dev` |
| Region | `northeurope` |
| Resource Group | `rg-dev` |
| BFF FQDN | `ordersphere-bff.kindtree-ed135723.northeurope.azurecontainerapps.io` |
| Issuer (`oidc-authority`) | `https://ordersphere-dev.eu.auth0.com/` |
| API audience | `https://api.ordersphere.dev` |
| Roles claim | `https://ordersphere.dev/roles` |

> **Resource Group:** The Aspire azd path derives the resource group from the environment name
> (`rg-<env>` → `rg-dev`) and ignores an `AZURE_RESOURCE_GROUP` set afterwards. The infrastructure
> therefore lives in `rg-dev`. For `azd deploy` to write the Container Apps into the same group,
> `AZURE_RESOURCE_GROUP=rg-dev` must be set (otherwise 404 `ResourceGroupNotFound`).

## Database schema

Each service calls `Database.Migrate()` at startup (ungated, not restricted to Development —
see e.g. `src/Services/Catalog/.../Program.cs`). In the cloud, each container therefore migrates its
own schema against the PostgreSQL Flexible Server on first start. No separate migration step is
required.

## Redis authentication (Entra ID)

`AddAzureManagedRedis` provisions Azure Managed Redis with Microsoft Entra ID authentication
(access keys disabled); the managed identity is assigned a data-access policy. The connection string
injected by Aspire contains **no** password. `Aspire.StackExchange.Redis` ships no Entra token logic —
a raw `ConnectionMultiplexer.Connect(connectionString)` therefore fails with
`NOAUTH - connection has not yet authenticated`.

The services consequently build the Redis connection through `AddOrderSphereRedisAsync`
(`OrderSphere.ServiceDefaults/RedisExtensions.cs`): for an Azure Redis endpoint without a password,
`Microsoft.Azure.StackExchangeRedis` obtains an Entra token via the user-assigned managed identity
(`AZURE_CLIENT_ID`) and renews it automatically. The token-authenticated `IConnectionMultiplexer` is
shared by DistributedCache (Catalog, Ordering, BFF), the DataProtection key ring, and the SignalR
backplane (BFF). Locally (Aspire dev container) the same code connects without credentials.

## Step by step

### 1. Sign in and create the environment
```powershell
azd auth login
azd env new dev --location northeurope --subscription <SUBSCRIPTION_ID>
azd env set AZURE_RESOURCE_GROUP rg-dev
```

> **Important:** Aspire parameters with a hyphen (`oidc-authority`,
> `payment-bypass-providers`, the four `*-secret`) must **not** be set via `azd env set` —
> `azd env set` writes to the `.env`, where hyphens in variable names are invalid
> (`unexpected character "-" in variable name`). `azd up` prompts for these parameters
> interactively on the first deploy and stores them correctly (non-secrets in `config.json`, secrets
> as a Key Vault reference). Only `AZURE_*` values (underscores) belong in the `.env`.

### 2. Obtain the client secrets from Auth0
Four confidential clients need their secret. Read each from the **Auth0 dashboard**
(*Applications → \<application\> → Settings → Client Secret*):

- **BFF** (Regular Web Application) — the interactive login client.
- **ordering-worker**, **notification-worker**, **payment-worker** (Machine-to-Machine
  applications) — `client_credentials` against the OrderSphere API.

The M2M client IDs must match the values set in `AppHost.cs`; align one side if they differ
(see [auth/role-model.md](auth/role-model.md)). To rotate a secret, use *Settings → Rotate Secret*
in the Auth0 dashboard.

> Real secrets do not belong in Git — only in Auth0 and the Azure Key Vault.

### 3. Deploy
```powershell
azd up
```
`azd up` prompts for the parameter values one after another:
- `oidc-authority` → the issuer URL (see Key facts above)
- `payment-bypass-providers` → `true`
- `bff-client-secret`, `ordering-worker-secret`, `notification-worker-secret`,
  `payment-worker-secret` → the values from step 2 (stored as Key Vault secrets)

It then provisions the infrastructure in `rg-dev` and deploys all 12 projects as
Container Apps. Only `ordersphere-bff` receives an external ingress (`WithExternalHttpEndpoints()` in
the AppHost). The answers are stored; later `azd up` runs do not prompt again.

### 4. Reconcile Auth0 against the BFF URL
After the deploy, determine the public BFF FQDN:
```powershell
azd show
```
On the **BFF application** in the Auth0 dashboard, add:
- Allowed Callback URL: `https://<bff-fqdn>/signin-oidc`
- Allowed Logout URL: `https://<bff-fqdn>/signout-callback-oidc`
- Allowed Web Origin: `https://<bff-fqdn>`
- Back-Channel Logout URI: `https://<bff-fqdn>/bff/backchannel-logout`

## Verification

1. `azd up` runs without errors; resources present in `rg-dev`.
2. Service logs show a successful JWKS fetch from the Auth0 issuer.
3. `https://<bff-fqdn>` → login redirect to Auth0 → successful return after step 4.
4. An authenticated API call (per-service audience validation against `https://api.ordersphere.dev`)
   returns 200.
5. A `client_credentials` token for `ordering-worker`/`payment-worker` with the real secret →
   a valid token accepted by the target service.

## CI/CD — `Deploy OrderSphere` workflow

Releases and DEV deployments run through the manual GitHub Actions workflow
`.github/workflows/release-deploy.yml` (`workflow_dispatch`, input `version`). One run bumps
`<VersionPrefix>` in `Directory.Build.props`, commits it to `master`, creates the tag `vX.Y.Z`,
then provisions and deploys the tagged revision to DEV via `azd`. It is deliberately manual
(cost control) — there is no auto-deploy on merge.

One-time setup (Azure-side OIDC and the four Auth0 secrets already exist from the first deploy):

| Item | Where | Value / note |
|---|---|---|
| GitHub Environment `dev` | Settings → Environments | Name must be exactly `dev` — the federated credential on the `ordersphere-sso-deploy` service principal trusts `repo:MoritzWaldau/OrderSphere:environment:dev`. |
| Repo secret `RELEASE_PAT` | Settings → Secrets | Fine-grained PAT (Contents: RW). `master` is PR-protected, so the bump commit needs a token whose owner is on the *bypass required pull requests* list. |
| Repo variable `AZURE_ENV_NAME` | Settings → Variables | `ordersphere-dev` |
| Repo variable `AZURE_LOCATION` | Settings → Variables | `germanywestcentral` |

The workflow logs in with `azd auth login --federated-credential-provider github`, reconstructs the
azd environment in the runner (the local `.azure/` is gitignored and there is no remote state),
seeds the six infra parameters (`oidc_authority`, `payment_bypass_providers`, the four
`*_secret` from GitHub secrets), then runs `azd provision` followed by `azd deploy`. The .NET 10
SDK is required for the container build performed by azd.

## Troubleshooting (actually encountered on the first deploy)

| Symptom | Cause | Resolution |
|---|---|---|
| `ConflictError: A vault with the same name already exists in deleted state` | An earlier, aborted `azd up` created the Key Vault and deleted it during cleanup; Key Vault soft-delete keeps the name reserved. | `az keyvault purge --name <vault> --location northeurope`, then `azd up` again. |
| `empty dotnet configuration output` (suggestion: `EnableSdkContainerSupport`) | Masks the real error of `dotnet publish` during the container build. Two observed triggers: (a) the Docker credential helper returns no ACR credentials; (b) a publish fails during a parallel build. | (a) see next row; (b) build the solution upfront with `dotnet build -c Release` (outputs warm) or deploy services individually with `azd deploy <service>`. |
| `docker-credential-desktop.EXE get … credentials not found in native keychain` | `~/.docker/config.json` has `credsStore: desktop`, but the Desktop keychain does not return the ACR token. | Remove the `credsStore` line from `~/.docker/config.json`, then `az acr login --name <acr>` (writes the token base64 directly into `config.json`). |
| Deploy step: `404 ResourceGroupNotFound: 'rg-ordersphere-dev'` | Infrastructure is in `rg-dev` (azd default `rg-<env>`); `AZURE_RESOURCE_GROUP` pointed to a non-existent group. | `azd env set AZURE_RESOURCE_GROUP rg-dev`, then `azd up`. |
| `/bff/login` → `500`, logs: `NOAUTH … connection has not yet authenticated` (Redis) | Azure Managed Redis enforces Entra ID auth; a raw Redis connection sends no token. | See section [Redis authentication](#redis-authentication-entra-id) — connection via `AddOrderSphereRedisAsync`. |
