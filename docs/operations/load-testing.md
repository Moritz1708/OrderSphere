# Load testing — checkout critical path

Test configuration lives in `tests/load/` (`checkout.test.yaml`, `checkout-loadtest.jmx`,
`ordersphere_users.csv`). It is checked in as code and validated for syntax only; it is not
executed automatically and provisions no Azure resource until run explicitly.

## Scenario

Product lookup → add to basket → checkout, against Catalog, Basket, and Ordering directly
(see [Auth strategy](#auth-strategy) for why the BFF is bypassed). One JMeter thread group,
parameterized by `virtualUsers` / `rampSeconds` / `loops`. Each iteration sends a fresh
`Idempotency-Key` (`${__UUID()}`) on `POST /api/v1/checkout`, so repeated checkout calls within
a run are not deduplicated against each other — this measures sustained throughput, not the
dedup guard. To test the dedup path specifically, pin a static `Idempotency-Key` per user in a
separate run.

## Auth strategy

The BFF (`src/Gateways/OrderSphere.Bff`) only supports interactive Auth0 Authorization Code
flow (`GET /bff/login` → redirect → cookie session); there is no dev-mode bypass or
client-credentials grant in `BffAuthenticationExtensions.cs`. Azure Load Testing's JMeter
engine cannot drive that redirect flow. The scenario therefore calls Catalog/Basket/Ordering
directly with a bearer token obtained out-of-band per virtual user (Auth0 Resource Owner
Password Grant against a dedicated load-test tenant/app, or pre-minted tokens from a script run
before the test), supplied via `ordersphere_users.csv`
(`bearerToken,productSlug` columns — replace the placeholder tokens before running).

This is a known gap: the load test does not exercise the BFF cookie/session layer. Closing it
would require either enabling Auth0 ROPG for a load-test-only application, or a dedicated
dev-mode auth handler behind a feature flag — out of scope for this pass.

## Running it

Requires an Azure Load Testing resource and the Azure CLI `load` extension:

```bash
az load test create \
  --test-id checkout-loadtest \
  --load-test-resource <alt-resource-name> \
  --resource-group <rg> \
  --load-test-config-file tests/load/checkout.test.yaml

az load test-run create \
  --test-id checkout-loadtest \
  --load-test-resource <alt-resource-name> \
  --resource-group <rg> \
  --test-run-id checkout-$(date +%Y%m%d%H%M)
```

Before running against a shared DEV environment, populate `ordersphere_users.csv` with real
tokens and confirm target URLs in `checkout.test.yaml` (`env.baseUrl` / `catalogUrl` /
`basketUrl`) point at that environment.

## Targets

| Metric | Target | Source |
|---|---|---|
| P95 response time | < 2000 ms | Matches the existing `P95 latency` alert in [operations.md](../operations.md) (`percentile(duration,95) > 2000`, 5 min window) |
| Error rate | < 1% | `failureCriteria` in `checkout.test.yaml` |
| Checkout success rate | ≥ 99.5% | Matches the SLO target in [slo.md](slo.md) (E7) once that lands |

`checkout.test.yaml`'s `autoStop` halts the run early if the error percentage exceeds 90% over
a 60-second window, to avoid burning load-test minutes against a clearly broken target.

## Chaos overlay

To combine load with fault injection, enable the opt-in chaos pipeline
(`ChaosOptions`/`AddOrderSphereChaos` in `src/Hosting/OrderSphere.ServiceDefaults/Chaos/`) on the
target environment via `Chaos:Enabled=true` before running the load test. See
`tests/OrderSphere.IntegrationTests/ChaosSagaCompensationTests.cs` for the equivalent
single-request proof that saga compensation holds under an injected Catalog-confirm conflict;
running the load scenario with chaos enabled extends that proof to concurrent load.
