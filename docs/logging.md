# Structured Logging

Binding conventions for log output across all OrderSphere services. Companion to
[operations.md](operations.md) (telemetry export, dashboards, alerts) and
[data-classification.md](data-classification.md) (which data is sensitive and why).

## Pipeline

`Microsoft.Extensions.Logging` with the OpenTelemetry logger provider. There is no Serilog and
none is planned: the OTel provider is already the single export path to every sink — the Aspire
dashboard and Seq locally, Azure Monitor in production — and it attaches `trace_id` / `span_id`
to every record. Adding a sink means adding an exporter, not a second logging pipeline
(see [Viewing logs locally](#viewing-logs-locally)).

Two extensions sit on top, both wired centrally in
`src/Hosting/OrderSphere.ServiceDefaults/Extensions.cs` and therefore active in all 16 hosts that
call `AddServiceDefaults()` (the Blazor WASM client is not one of them — see
[Browser logs](#browser-logs)):

| Concern | Package | Entry point |
|---|---|---|
| Enrichment | `Microsoft.Extensions.Telemetry` | `builder.Logging.EnableEnrichment()` |
| Redaction | `Microsoft.Extensions.Compliance.Redaction` | `builder.Logging.EnableRedaction()` |

`EnableEnrichment()` replaces the default `ILoggerFactory` with `ExtendedLoggerFactory`.
Enrichment tags are written into the log-record state, which the OpenTelemetry provider reads as
attributes — so enriched fields appear as Custom Dimensions in Application Insights with no
per-service configuration.

## Field set

Every record carries these without the call site doing anything:

| Field | Source | Present in |
|---|---|---|
| `service.name`, `service.version`, `deployment.environment` | OTel `Resource` | all |
| `trace_id`, `span_id` | OTel logger provider | all (when a trace is active) |
| `service_instance_id`, `build_version` | `OrderSphereStaticLogEnricher` | all |
| `tenant_id` | `AmbientTenantContext` via `OrderSphereLogEnricher` | requests with an `org_id` claim, and message loops |
| `correlation_id` | `AmbientCorrelationContext` via `OrderSphereLogEnricher` | requests, message loops and background loops |
| `user_id` | `IHttpContextAccessor` (`sub` claim) via `OrderSphereLogEnricher` | authenticated HTTP requests |
| `client_ip_hash` | `RequestContextEnrichmentMiddleware` | HTTP requests |
| `message_id`, `event_type`, `queue` | `MessageProcessingScope` | Service Bus message loops |

The enricher reads `AsyncLocal` slots rather than `HttpContext`. That is the whole reason worker
records carry the same fields as API records: the message loop opens the ambient scopes and the
same singleton enricher picks them up, with no worker code aware of logging infrastructure. The
one exception is records written after the ambient scopes have unwound but while the request is
still being handled — see *Unhandled exceptions correlate too* under Correlation.

Three scopes fill those slots, one per kind of work:

| Opened by | Covers | Tenant source |
|---|---|---|
| `RequestContextEnrichmentMiddleware` | HTTP requests, after authentication | `org_id` claim (ADR 0012) |
| `MessageProcessingScope` | Service Bus message loops | `TenantId` on the inbound event |
| `BackgroundOperationScope` | one iteration of a timer-driven loop | none — background work is cross-tenant |

`tenant_id` is **absent**, not `Guid.Empty`, when a request carries no `org_id` (anonymous
traffic, or a deployment with Auth0 Organizations not enabled) and on background loops. An
all-zero GUID would be indistinguishable from a real single-organisation tenant, so the field is
omitted instead. `ITenantContext` still resolves `TenantId.Default` for persistence, so EF
stamping and the tenant query filter are unaffected by that choice.

The request scope is not only a logging concern: `ITenantContext`, EF audit stamping, the tenant
query filter and `IntegrationEvent.TenantId`'s default all read the same slot. That is why a
command handler never assigns `TenantId` on an event it stages — the middleware's scope is open
for the whole of endpoint execution, so the default is already correct. An explicit assignment is
a smell.

### Naming

- Enrichment and scope keys: `snake_case` (`tenant_id`), matching OpenTelemetry convention.
- Message-template placeholders: `PascalCase` (`{OrderId}`), matching the `ILogger` convention.
- Source-generated `[LoggerMessage]` parameters: `camelCase`, which is what the generator emits
  as the property name.

## Correlation

One value, end to end:

1. The API Gateway sets `X-Request-Id` when absent, **seeded from the current trace id**
   (`ApiGateway/Program.cs`). Client-visible id, `correlation_id` and `trace_id` are the same
   string.
2. `RequestContextEnrichmentMiddleware` opens `AmbientCorrelationContext` from that header and
   echoes it on the response. It also stashes the id on `HttpContext.Items`; see the note on
   unhandled exceptions below.
3. `CorrelationPropagationHandler`, registered on `ConfigureHttpClientDefaults`, puts it on every
   outgoing service-to-service call.
4. `EventBusDiagnostics.Inject` writes it onto the Service Bus message as `x-request-id`.
5. `MessageProcessingScope` reads it back on the consuming side, falling back to the message's
   `traceparent` trace id and finally to the message id.
6. Across the outbox, the row persists the correlation id in its own `CorrelationId` column
   alongside `TraceParent`, and `EventBusDiagnostics.RestorePublishParent` reopens the scope from
   it.
7. Timer-driven work has nothing to inherit, so `BackgroundOperationScope` starts a fresh trace
   per iteration and seeds the correlation id from it — the same shape as step 1.

Step 6 used to derive the correlation id from the restored trace id instead, on the grounds that
step 1 seeds one from the other. That equality does not hold: the gateway honours a
client-supplied `X-Request-Id`, and step 5 falls back to the message id — after either, the
derivation silently substituted a different id at the outbox boundary and split the chain in two.
The column is nullable, and rows written before it existed still fall back to the trace id, which
is what they were correlated by.

`IntegrationEvent.CorrelationId` is unrelated: it is a business idempotency key. Where it is
logged it is named `EventCorrelationId` to keep the two apart. `OutboxMessage.CorrelationId` is
the log-correlation id described above.

Two consequences worth stating, because both were gaps until recently:

- **A client may choose the correlation id.** It is caller-controlled input that ends up in a
  structured log field, so it is length-capped when persisted. Do not render it as markup.
- **Background loops correlate too.** The outbox dispatcher's own failure records, the webhook
  delivery loop, the scheduled jobs and the DLQ monitor each open a scope per iteration. Without
  it, the records most wanted when a flow has stalled were the ones a `correlation_id` query could
  not return.
- **Unhandled exceptions correlate too.** `UseExceptionHandler()` must sit outside
  `UseOrderSphereRequestLogging()` — it can only catch what runs inside it — so an exception has
  already unwound past the enrichment middleware, disposing both ambient scopes, by the time
  `ExceptionHandlerMiddleware` logs it. The `HttpContext` outlives the scopes and is the same
  instance in both, so `OrderSphereLogEnricher` falls back to `HttpContext.Items` for records
  written after the unwind. Without that fallback the unhandled 500 — the record an operator
  reaches for first — was the single record on the request path with no `correlation_id`.
  `tests/OrderSphere.IntegrationTests/Logging/LogEnrichmentTests.cs` pins this.

## Levels

| Level | Use | Notes |
|---|---|---|
| `Trace` | never enabled in production | |
| `Debug` | flow detail: handler entry, message received, validation failures | default off in production |
| `Information` | the business outcome, **once** per unit of work | not per step |
| `Warning` | expected failure, including every `Result` failure | the normal level for "not found", "already processed", "declined" |
| `Error` | unexpected exception, or risk of data loss | includes dead-lettering |
| `Critical` | the process can no longer do its job | |

Consequences worth stating explicitly, because they are easy to get wrong:

- **A `Result` failure is a `Warning`, not an `Error`.** An empty cart, an unknown coupon, a
  missing payment to refund — these are outcomes the code handles deliberately.
- **`LoggingBehavior` logs handler success at `Debug`.** It fires for every query on every API,
  and the duration it reports is already in the request span and the
  `ordersphere.mediatr.request.duration` histogram. An `Information` record per read would be
  duplication at the single highest-volume point in the system.
- **Message processors emit one `Information` record per message**, at the end. Receipt is
  `Debug`; the identifiers that used to be repeated in the text are structured fields on the
  scope.
- Never log an exception's `Message` as the template. Pass the exception as the first argument:
  `logger.LogWarning(ex, "...")`.

## PII

The rule is in [data-classification.md](data-classification.md); this is how it is enforced.

**Redaction applies only to `[LoggerMessage]` parameters carrying a classification attribute.**
A plain `logger.LogInformation("... {Email}", email)` is *not* redacted. Personal data must
therefore be logged through a source-generated method.

```csharp
[LoggerMessage(EventId = 8002, Level = LogLevel.Information,
    Message = "Confirmation email sent for order {orderId}.")]
public static partial void OrderConfirmationEmailSent(
    this ILogger logger, Guid orderId, [DirectPii] string recipient);
```

Attributes live in `BuildingBlocks.Domain/Compliance/OrderSphereDataClassifications.cs` and map to
the tiers in data-classification.md:

| Attribute | Tier | Redactor | Rationale |
|---|---|---|---|
| `[DirectPii]` | T1 — name, email, address | HMAC (key id 1) | groupable per customer, never readable |
| `[PseudonymousId]` | T2 — session id, client IP | HMAC (key id 2) | separate key id, so T1 and T2 cannot be cross-correlated |
| `[FreeText]` | T4 — chat transcripts, review bodies | erasing | highest re-identification risk, no operational value |

T3 (financial) has deliberately **no** classification. Amounts and PSP references are not personal
data, are needed verbatim for reconciliation, and the erasing fallback redactor would destroy
them.

The HMAC key comes from `Logging:Redaction:HmacKey`. It is per-deployment: two environments
produce unrelated hashes. Without configuration a process-lifetime random key is generated —
redaction still holds, only cross-restart correlation is lost, and each replica hashes the same
input differently, so the grouping the HMAC redactor was chosen for stops working across a scaled
deployment.

The AppHost injects it into every project as the `logging-redaction-hmac-key` parameter. Deployed
environments take the value from `infra.parameters` in `.github/workflows/release-deploy.yml`;
locally it comes from user-secrets on the AppHost, like every other secret parameter (see
*Secret rotation* in
[architecture.md](architecture.md#internal-service-to-service-authentication)). Set it once per
clone — any stable value will do, the point is only that it does not change between restarts:

```bash
dotnet user-secrets set "Parameters:logging-redaction-hmac-key" "$(openssl rand -base64 32)" --project src/Hosting/OrderSphere.AppHost
```

Two identifiers stay readable by design:

- `user_id` (the Auth0 `sub`) — an opaque pseudonymous id; investigations need to pivot from an
  audit record to that user's other records.
- `client_ip` is **not** logged; `client_ip_hash` (truncated SHA-256) is, which still groups
  requests per client.

`SecurityAuditLogger` writes each field as its own property with `[PseudonymousId]` on session id
and IP, so audit records are queryable by field instead of parsed out of a delimited string.

## EventId ranges

Assigned per area so a record's origin is identifiable without the category string.

| Range | Area |
|---|---|
| 1000–1099 | `BuildingBlocks.Domain` (MediatR behaviors) |
| 1100–1199 | `EventBus.AzureServiceBus` (processors, outbox, DLQ) |
| 1200–1299 | `ServiceDefaults` (security audit, request pipeline) |
| 2000–2999 | Catalog |
| 3000–3999 | Ordering |
| 4000–4999 | Basket |
| 5000–5999 | Payment |
| 6000–6999 | Invoicing |
| 7000–7999 | Webhooks |
| 8000–8999 | Notification |
| 9000–9999 | UserProfile |
| 10000–10999 | Partners |
| 11000–11999 | Advisory |
| 12000–12999 | Gateways (ApiGateway, BFF) |

## When to use `[LoggerMessage]`

Required for anything touching classified data (redaction depends on it), and for the hot paths:
the event bus, the Service Bus processors, the MediatR behaviors, and the cross-service HTTP
clients. Elsewhere plain `logger.LogX` with a constant template is fine — the enrichment and level
policy apply either way. Never build the template with interpolation or concatenation; the
template must be a compile-time constant.

## Configuration

Every host's `appsettings.json` carries the same `Logging:LogLevel` shape:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "OrderSphere": "Information"
    }
  },
  "OpenTelemetry": { "TracesSampleRatio": 1.0 }
}
```

Service-specific overrides (`Yarp`, `Microsoft.EntityFrameworkCore`,
`Microsoft.AspNetCore.Authentication`) are added on top. `appsettings.Development.json` sets
`OrderSphere` to `Debug`.

`Microsoft.EntityFrameworkCore.Database.Command` is filtered to `Warning` in code
(`ConfigureOpenTelemetry`), so the database story comes from DB spans rather than log spam.

`OpenTelemetry:TracesSampleRatio` is parsed with `InvariantCulture` and clamped to `[0,1]`.
Both matter: configuration is culture-invariant, but a plain `double.TryParse` uses the host
culture, where `"1.0"` on a de-DE machine parses as `10` and crashes the sampler at startup.
Lower the ratio (0.1–0.2) in production to control cost.

## Viewing logs locally

Two sinks receive every record in local development. Both are fed from the same OpenTelemetry
pipeline; adding Seq did not change or replace the dashboard export.

| Sink | Wiring | Use it for |
|---|---|---|
| Aspire dashboard | `OTEL_EXPORTER_OTLP_ENDPOINT`, injected by the AppHost | resource state, metrics, a quick look at one service |
| Seq (`http://localhost:<port>`) | `AddSeq` in the AppHost, `AddSeqEndpoint` in ServiceDefaults | querying and filtering logs and traces |

The dashboard has no query language and discards its data when the AppHost restarts. Seq indexes
every property, including the enrichment fields, and `WithDataVolume()` keeps the data across
restarts. Reach it from the resource list in the dashboard.

Because the enrichment tags are real log-record attributes rather than text in the message, they
are directly queryable:

```
correlation_id = '0af7651916cd43dd8448eb211c80319c'
tenant_id = '...' and @Level in ['Warning', 'Error']
event_type like 'Order%' and @Exception is not null
queue = 'payment-requested' and @Level = 'Error'
```

The first of those is the point of the correlation chain above: one expression returns the log
records of a whole checkout across the gateway, the APIs, the outbox and the workers.

Two names in the field table above do **not** work verbatim as Seq filters, because Seq separates
OTLP resource attributes and trace identifiers from the event's own properties. Both forms fail by
returning zero rows rather than an error, which is the failure mode worth knowing about:

| Field table name | In a Seq filter |
|---|---|
| `service.name`, `service.version`, `deployment.environment` | `@Resource.service.name`, and so on |
| `trace_id`, `span_id` | `TraceId`, `SpanId` |

Everything the enrichers add — `correlation_id`, `tenant_id`, `user_id`, `client_ip_hash`,
`build_version`, `service_instance_id` and the `MessageProcessingScope` fields — is a plain event
property and is queried under exactly the name in the table.

Wiring notes:

- The Seq resource is added in **run mode only**. Production telemetry goes to Application
  Insights; a developer-tool container has no place in the published manifest.
- `AddSeqEndpoint` registers an *additional* OTLP exporter (logs and traces) rather than
  replacing `UseOtlpExporter()`. It activates only when the `seq` connection string is present,
  so tests and production are unaffected.
- Its health check is switched off deliberately. A developer-tooling sink must never be able to
  report a service as unready and stall the Aspire `WaitFor` chains.
- Seq takes logs and traces over OTLP, **not metrics** — those stay in the Aspire dashboard.

### Querying Seq from an agent

Seq 2026.1 ships a first-party MCP server, delivered through the `seqcli` client rather than
built into the server. It gives an agent read access to the same queries a developer would run.

```bash
dotnet tool install --global seqcli
seqcli mcp install --agent <agent>
```

It reads `SEQCLI_CONNECTION_SERVERURL` and `SEQCLI_CONNECTION_APIKEY`. The local Seq container
runs with `SEQ_FIRSTRUN_NOAUTHENTICATION`, so no API key is needed against it; a hosted instance
needs one with Read permission. `seqcli mcp install --help` lists the supported agents.

The AppHost pins the image to `2026.1` for this reason — Aspire 13.5.3 still defaults to
`datalust/seq:2025.2`, which predates the MCP release.

### OrderSphere dashboard

[`docs/seq/create-ordersphere-dashboard.ps1`](seq/create-ordersphere-dashboard.ps1) builds an
"OrderSphere" dashboard covering the fields above: request/error/warning/exception counts,
events and warnings by service, Service Bus queue and integration-event volume, tenant and
correlation-chain breakdowns, and a live feed of recent warnings and errors. Seq's data volume
persists it across AppHost restarts, so this is a one-time setup per environment (or after a
volume reset):

```powershell
./docs/seq/create-ordersphere-dashboard.ps1 -ServerUrl http://localhost:<seq-port>
```

The port comes from the `seq` resource's http endpoint in the Aspire dashboard. Re-running the
script updates the existing dashboard in place rather than creating a duplicate. Every chart
query is a plain Seq query and can be run standalone with `seqcli query -q "..."` — see the
script's comments for the two rough edges this ran into: `ChartQuery.SignalExpression` 500s on
`POST /api/dashboards/` on this Seq build (worked around by inlining the built-in signals'
filter text instead), and a nested OTel resource attribute like `service.name` must be grouped
by as `@Resource.service.name` (dot path) — `@ra['service.name']` parses but silently returns
null.

## Browser logs

Blazor WASM logs go to the browser console only and are **not exported**. Aspire's Blazor hosting
integration (`ProxyBlazorTelemetry`) is not available in the version this repository uses
(`Aspire.Hosting` 13.5.3 contains no Blazor types, and no official `Aspire.Hosting.Blazor`
package is published), and exposing the dashboard's OTLP endpoint to the browser by hand would
put dashboard credentials in browser-visible configuration.

The bridge in the meantime: `LoggingHandler` logs the `X-Request-Id` the gateway echoes on failed
and slow calls. A user quoting that id from the console links directly to the server-side records
and the trace.

## Testing

`Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`) is available in the test projects.
Prefer building a real host with `AddServiceDefaults()` and asserting on
`GetFakeLogCollector().GetSnapshot()` — that exercises the actual wiring rather than a class in
isolation. See:

- `tests/OrderSphere.IntegrationTests/Logging/LogEnrichmentTests.cs`
- `tests/OrderSphere.IntegrationTests/Logging/TracesSampleRatioTests.cs`
- `tests/OrderSphere.Notification.Tests/Logging/LogRedactionTests.cs` — the regression guard that
  keeps customer email addresses out of the log stream
- `tests/OrderSphere.EventBus.AzureServiceBus.Tests/MessageProcessingScopeTests.cs`
