# Changelog

All notable changes to OrderSphere are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Entries are maintained
manually.

## [Unreleased]

### Added
- Invoicing service: generates invoice PDFs (QuestPDF) on order placement, stores them in Blob
  storage, and sends an invoice-ready email. Customers can view and download their invoice from the
  order detail page.
- `BuildingBlocks.Infrastructure`: shared Azure Blob storage implementation (relocated from Catalog),
  now consumed by both Catalog and Invoicing.
- Repository documentation: `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, and this changelog.
- README status, deployment, security, and contributing sections with CI/security badges.
- README: local-secrets setup, configuration reference, repository layout, testing/quality,
  screenshots, and roadmap sections.
- `docs/README.md` documentation index.
- Architecture Decision Records under `docs/adr/` (0001–0006).
- `docs/glossary.md` (ubiquitous language) and `docs/operations.md` (observability runbook).
- `SECURITY.md`: threat-model / trust-boundaries section.

### Changed
- Corrected AppHost project path to `src/Hosting/OrderSphere.AppHost` across README, `CLAUDE.md`,
  and `docs/architecture.md`; corrected `OrderSphere.Web` path to `src/Frontend/OrderSphere.Web`.
- `contracts/CONVENTIONS.md` rewritten to describe the actual HTTP-client + `BuildingBlocks.Contracts`
  architecture; gRPC/OpenAPI/NuGet contract packages marked out of scope.
- `docs/architecture.md`: corrected the Service Bus queue inventory to match `AppHost.cs`.
- `docs/deploy-ordersphere.md`: fixed step numbering.
- `Order` status transitions (`Confirm`, `MarkShipped`, `MarkDelivered`, `Cancel`) and
  `PaymentRecord` transitions return `Result` and reject invalid transitions instead of throwing
  or applying them.
- `OrderStatusChangedIntegrationEvent` carries an optional `CustomerId` (additive).
- Stripe client: 30 s timeout with 2 network retries (SDK default 80 s), keeping one payment run
  inside the Service Bus lock renewal window.

### Fixed
- Stripe calls carry deterministic idempotency keys; a redelivered payment request no longer
  creates a second PaymentIntent. Only declines and invalid requests are reported as failures;
  transient faults are retried through Service Bus redelivery.
- A failed capture releases the authorization and keeps the PaymentIntent id on the payment record.
- The Stripe webhook is reachable (`/webhooks/stripe` on the BFF), finds payments by order or intent,
  asks Stripe to retry while the record does not exist yet, rejects a missing signature with 400
  instead of 500, and applies only valid status transitions.
- A payment result for an already cancelled order no longer re-confirms it; a captured payment on a
  cancelled order is refunded.
- Admin coupon management is routed through the API Gateway (was 404).

### Security
- Webhook subscriptions only receive events of their own customer (previously every subscriber of
  an event type received all customers' events).
- Webhook target URLs are restricted to public HTTPS hosts, checked on save and again for every
  resolved address at connect time; redirects are not followed; failed deliveries no longer store
  the target's response body.
- `/bff/login` only accepts a local `returnUrl` (open redirect).
