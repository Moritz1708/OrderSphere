# OrderSphere — budget Azure deployment ($80/month target)

A second, independent azd environment alongside `ordersphere-dev` (see
[deploy-ordersphere.md](deploy-ordersphere.md)), sized to fit a self-funded ~$80/month budget
instead of dev/test convenience. Same AppHost, same Bicep-from-manifest model — only the resource
SKUs and Container Apps scaling differ, via environment-scoped opt-ins so `ordersphere-dev` is
unaffected.

## What's different from `ordersphere-dev`

| Aspect | `ordersphere-dev` | Budget environment |
|---|---|---|
| Postgres | Container in Container Apps (`AddPostgres`) | Azure PostgreSQL Flexible Server, Burstable B1ms |
| Redis | Azure Managed Redis, default SKU | Azure Managed Redis, Balanced B0 (pinned) |
| Service Bus | Azure Service Bus, default SKU | Azure Service Bus, Basic tier (pinned) |
| Container Apps scaling | azd/Container-Apps defaults | `ordersphere-bff` warm (`MinReplicas: 1`); every other service scales to zero; queue-consuming services wake via a KEDA `azure-servicebus` scale rule per queue |
| Log Analytics | Uncapped ingestion | 1 GB/day cap |

Estimated monthly cost (West Europe/EU West retail pricing, Consumption plan): Postgres Flexible
B1ms ≈ $18 (compute + storage), Redis Balanced B0 ≈ $14, Service Bus Basic ≈ $1 (pay-per-operation,
no base fee), Container Registry Basic ≈ $5, Container Apps compute (mostly scaled to zero) ≈
$15–25 depending on traffic, Blob Storage/Communication Services ≈ $1–2. Total ≈ $55–70/month,
leaving headroom under $80 for growth. This is an estimate, not a guarantee — see
[Budget alert](#budget-alert) below for the actual enforcement mechanism.

## Known trade-offs (accepted for this environment)

- **No HA.** Single-instance Postgres (no zone redundancy), no Redis geo-replication.
- **Cold starts.** Any service other than `ordersphere-bff` that has been idle scales to zero;
  the next request pays a cold-start latency (a few seconds). A request that fans out through a
  chain of idle services (Bff → ApiGateway → Catalog, say) pays the cold start of each link in the
  chain on the first hit; subsequent requests are fast once those services are warm.
- **Queue-consumer wake-up depends on an unverified KEDA scale rule.** `ordersphere-ordering-worker`,
  `ordersphere-notification-worker`, `ordersphere-webhooks-worker`, `ordersphere-advisory`,
  `ordersphere-invoicing`, and `ordersphere-payment-worker` scale to zero too, each with one
  `azure-servicebus` scale rule per queue it consumes (queue names come from each
  `Workers/*Processor.cs`'s `QueueName` constant). **This must be verified after the first real
  deploy** (see [Verification](#verification)) — if a rule doesn't wake its app, messages queue up
  silently until unrelated traffic happens to activate it.

## Prerequisites

Same as [deploy-ordersphere.md](deploy-ordersphere.md#prerequisites), plus: an Azure subscription
whose policies allow Azure PostgreSQL Flexible Server (unlike the one behind `ordersphere-dev`).

## Step by step

### 1. Create the environment

```powershell
azd auth login
azd env new ordersphere-budget --location germanywestcentral --subscription <SUBSCRIPTION_ID>
azd env set AZURE_RESOURCE_GROUP rg-ordersphere-budget
```

Region: `germanywestcentral` matches the `AZURE_LOCATION` repo variable used by
`release-deploy.yml`. `deploy-ordersphere.md` uses `northeurope` for `dev` — the two documents
intentionally differ; pick whichever region fits your subscription/latency needs.

### 2. Opt into the budget-sized resources

```powershell
azd env set Deployment__UseManagedPostgres true
azd env set BUDGET_SCALING true
```

Both default to the `ordersphere-dev` behavior (container Postgres, no custom scaling) when unset
— these two lines are what make this environment different.

### 3. Auth0

Two options, neither is set up yet — decide before the first `azd up`:

- **Reuse the `ordersphere-dev` Auth0 tenant/applications.** Simplest: after deploying, add the new
  BFF's FQDN as an *additional* callback/logout/origin/back-channel-logout URL on the existing BFF
  application (additive, same pattern as step 4 in `deploy-ordersphere.md`), and reuse the same
  four client secrets.
- **Separate Auth0 applications for this environment.** Cleaner isolation, more setup — create a
  parallel BFF + three M2M applications, update the M2M client IDs in `AppHost.cs` if they must
  differ per environment (they currently don't vary by environment).

### 4. Deploy

```powershell
azd up
```

Same parameter prompts as `ordersphere-dev` (`oidc-authority`, `payment-bypass-providers`, four
client secrets) — see [deploy-ordersphere.md, step 3](deploy-ordersphere.md#3-deploy).

### 5. Reconcile Auth0 against the new BFF URL

Same as [deploy-ordersphere.md, step 4](deploy-ordersphere.md#4-reconcile-auth0-against-the-bff-url).

## Budget alert

SKU sizing bounds the *expected* cost but not a spike from unexpected traffic. After the first
`azd provision`, create an Azure Cost Management budget on the resource group:

```powershell
az consumption budget create --budget-name ordersphere-budget-80 `
  --resource-group rg-ordersphere-budget --amount 80 --time-grain Monthly `
  --start-date <first-of-month> --end-date <one-year-later> `
  --notifications-enabled true `
  --notification-key actual80 --notification-threshold 80 --notification-operator GreaterThanOrEqualTo `
  --contact-emails <your-email>
```

Verify the exact flag syntax against the installed `az` CLI version (`az consumption budget create
--help`) — it has drifted across CLI versions.

## Verification

1. `az postgres flexible-server show --name <name> --resource-group rg-ordersphere-budget --query sku`
   → `Standard_B1ms` / `Burstable`.
2. `az servicebus namespace show --name <name> --resource-group rg-ordersphere-budget --query sku.name`
   → `Basic`.
3. Confirm the Azure Managed Redis cluster's SKU is `Balanced_B0` (resource type
   `Microsoft.Cache/redisEnterprise`; exact `az` subcommand — `az redisenterprise` or
   `az resource show --resource-type Microsoft.Cache/redisEnterprise` — depends on installed CLI
   extensions, verify at execution time).
4. `az containerapp show --name ordersphere-bff --resource-group rg-ordersphere-budget --query properties.template.scale`
   → `minReplicas: 1`.
5. `az containerapp show --name ordersphere-catalog --resource-group rg-ordersphere-budget --query properties.template.scale`
   → `minReplicas: 0`.
6. `az containerapp show --name ordersphere-ordering-worker --resource-group rg-ordersphere-budget --query properties.template.scale.rules`
   → all 5 `azure-servicebus` rules present (`sb-orders`, `sb-payment-results`,
   `sb-payment-refunds`, `sb-order-history`, `sb-erasure-ordering`), not just the last one applied.
7. **Queue wake-up test:** with `ordersphere-payment-worker` at 0 replicas, place a message on
   `payment-requests` (e.g. trigger a checkout through the app, or `az servicebus queue message
   send` directly) and confirm a replica starts and drains it
   (`az containerapp revision list` / Log Analytics).
8. Functional smoke test: open the BFF FQDN (`azd show`), complete the Auth0 login redirect,
   confirm an authenticated API call against a cold service (e.g. Catalog) succeeds — note the
   observed cold-start latency.
9. `az consumption budget show --budget-name ordersphere-budget-80 --resource-group rg-ordersphere-budget`
   confirms the alert exists.
10. After 3–5 days of operation, compare actual spend (Cost Management in the Azure Portal, or `az
    consumption usage list`) against the ≈$55–70/month estimate.
