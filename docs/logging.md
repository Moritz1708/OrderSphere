# Structured Logging

Binding conventions for log output across all OrderSphere services. Companion to
[operations.md](operations.md) (telemetry export, dashboards, alerts) and
[data-classification.md](data-classification.md) (which data is sensitive and why).

## Pipeline

`Microsoft.Extensions.Logging` with the OpenTelemetry logger provider. There is no Serilog and
none is planned: the OTel provider is already the single export path to the Aspire dashboard and
Azure Monitor, and it attaches `trace_id` / `span_id` to every record.

Two extensions sit on top, both wired centrally in
`src/Hosting/OrderSphere.ServiceDefaults/Extensions.cs` and therefore active in all 22 hosts:

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
| `tenant_id` | `AmbientTenantContext` via `OrderSphereLogEnricher` | when a tenant scope is open |
| `correlation_id` | `AmbientCorrelationContext` via `OrderSphereLogEnricher` | requests and message loops |
| `user_id` | `IHttpContextAccessor` (`sub` claim) via `OrderSphereLogEnricher` | authenticated HTTP requests |
| `client_ip_hash` | `RequestContextEnrichmentMiddleware` | HTTP requests |
| `message_id`, `event_type`, `queue` | `MessageProcessingScope` | Service Bus message loops |

The enricher reads `AsyncLocal` slots rather than `HttpContext`. That is the whole reason worker
records carry the same fields as API records: the message loop opens the ambient scopes and the
same singleton enricher picks them up, with no worker code aware of logging infrastructure.

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
   echoes it on the response.
3. `CorrelationPropagationHandler`, registered on `ConfigureHttpClientDefaults`, puts it on every
   outgoing service-to-service call.
4. `EventBusDiagnostics.Inject` writes it onto the Service Bus message as `x-request-id`.
5. `MessageProcessingScope` reads it back on the consuming side, falling back to the message's
   `traceparent` trace id and finally to the message id.
6. Across the outbox — where only `traceparent` is persisted on the row —
   `EventBusDiagnostics.RestorePublishParent` reopens the correlation scope from the restored
   trace id. Because step 1 seeds from the trace id, this is the same value; no outbox column and
   no schema change were needed.

`IntegrationEvent.CorrelationId` is unrelated: it is a business idempotency key. Where it is
logged it is named `EventCorrelationId` to keep the two apart.

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
redaction still holds, only cross-restart correlation is lost.

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
