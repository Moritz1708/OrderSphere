# Data Classification

Inventory of personal and sensitive data at rest across OrderSphere services, by sensitivity
tier. Basis for D6 (Epic #62) scoping decisions on encryption-at-rest strength; also the
reference for reviewing GDPR erasure coverage (D1, [#88](https://github.com/MoritzWaldau/OrderSphere/issues/88)).

## Sensitivity tiers

| Tier | Definition | Handling implication |
|---|---|---|
| **T1 — Direct PII** | Directly identifies a natural person (name, email, postal address). | Erasure-capable; candidate for column-level encryption. |
| **T2 — Pseudonymous identifier** | Identifies a person only via a join to another store (Auth0 `sub`, internal customer ID). Not directly identifying on its own. | Lower risk than T1, but still personal data under GDPR Art. 4(1) since it is linkable. |
| **T3 — Financial** | Payment amounts, PSP references, invoice totals. Not PII by itself, but commercially sensitive and typically has its own statutory retention period that can outlive an erasure request. | Retain per commercial-law retention; do not delete on erasure, only decouple from PII. |
| **T4 — Free text, PII risk** | Human-authored free text (chat transcripts, review bodies, staff notes) that can incidentally contain PII but isn't structured as such. | Highest re-identification risk per byte stored; hardest to anonymize field-by-field. |
| **T5 — Non-sensitive** | Operational/structural data (status enums, timestamps, opaque IDs, product data). | No special handling. |

## UserProfile

| Entity | Field | Tier | Erasure mechanism |
|---|---|---|---|
| `CustomerProfile` | `Subject` | T2 | `Anonymize()` cascades to soft-delete |
| `CustomerProfile` | `DisplayName`, `Email` | T1 | `Anonymize()` overwrites with placeholders |
| `CustomerProfile` | `NotificationPreferences.ConsentedAt` | T5 (consent metadata) | n/a |
| `SavedAddress` | `FirstName`, `LastName`, `Street`, `City`, `PostalCode`, `Country` | T1 | `Anonymize()` overwrites, soft-deletes |

## Ordering

| Entity | Field | Tier | Erasure mechanism |
|---|---|---|---|
| `Order.Address` (value object) | `FirstName`, `LastName`, `Street`, `City`, `PostalCode` | T1 | `Address.Erased(country)` — erases all but `Country` |
| `Order.Address` | `Country` | T5 (retained for tax/reporting) | — |
| `OrderHistoryEntry` (read model) | `CustomerEmail` | T1 | **None.** Not an `AuditableEntity`; a denormalized PII copy outside the write-side aggregate with no erasure path. |

## Payment

| Entity | Field | Tier | Erasure mechanism |
|---|---|---|---|
| `PaymentRecord` | `CustomerEmail` | T1 | `AnonymizeCustomerEmail()` |
| `PaymentRecord` | `Amount`, `TransactionId`, `PaymentMethod` | T3 | Retained deliberately (financial record) |
| `PaymentRecord` | `FailureReason` | T4 | Not covered by `AnonymizeCustomerEmail()`; PSP error text may echo cardholder-adjacent detail. |

## Invoicing

| Entity | Field | Tier | Erasure mechanism |
|---|---|---|---|
| `Invoice` | `CustomerName`, `CustomerEmail` | T1 | `AnonymizeCustomer()` |
| `Invoice` | `Total`, `NetAmount`, `TaxRate`, `TaxAmount`, adjusted totals | T3 | Retained (statutory commercial retention) |
| `Invoice` | `BlobPath` (PDF) | T1 (indirect) | **None.** The generated PDF itself contains the original name/address; `AnonymizeCustomer()` only touches the row, not the blob. |
| `InvoiceAdjustment` | `Reason` | T4 | No erasure mechanism |
| `InvoiceAdjustment` | `AppliedBy` | T2 (operator, not customer) | No erasure mechanism (lower priority — internal staff data) |

## Advisory

| Entity | Field | Tier | Erasure mechanism |
|---|---|---|---|
| `Conversation` | `CustomerSub` | T2 | **None.** |
| `Conversation` | `SerializedSession` | T4 (high severity — full chat history serialized) | **None.** |
| `ConversationMessage` | `Text` | T4 (high severity — per-turn transcript) | **None.** |

Advisory has no erasure path at all today. Per the D1 plan, the recommended remediation for
free-text conversational data is hard-delete of the conversation rows on erasure, not partial
field anonymization — T4 text cannot be reliably scrubbed field-by-field.

## Catalog

| Entity | Field | Tier | Notes |
|---|---|---|---|
| `ProductReview` | `CustomerId` | T2 | Pseudonymous; not directly identifying without a UserProfile join. |
| `ProductReview` | `Title`, `Body` | T4 | Reviewer-authored free text. |
| `Product`, `Category`, `Brand`, `StockReservation` | — | T5 | No personal data. |

## Basket

`Cart.CustomerId` is T2 (pseudonymous). No other PII — `CartItem` holds only `ProductId`/`Quantity`.
Confirms the D1 finding that Basket has no PII at rest requiring an erasure handler.

## Notification

Worker-only; the sole persisted state is `InboxMessage` dedupe rows keyed by event ID. No
customer-identifying fields. No PII at rest.

## Known erasure gaps (input to D1 follow-up, not D6)

These were surfaced by this inventory and are erasure-coverage gaps, not encryption gaps — listed
here because they affect which fields are worth encrypting at the column level (a field with no
erasure path is a field that persists indefinitely, raising the cost of a breach):

1. **Ordering `OrderHistoryEntry.CustomerEmail`** — denormalized read-model copy, no erasure method.
2. **Invoicing `Invoice.BlobPath` PDF** — the row's PII can be anonymized but the underlying blob
   document is not regenerated or deleted.
3. **Invoicing `InvoiceAdjustment.Reason`** — free-text, no erasure method (lower priority).
4. **Advisory `Conversation`/`ConversationMessage`** — no erasure mechanism at all; `CustomerSub`,
   `SerializedSession`, and `Text` all persist indefinitely today.

## Relationship to encryption-at-rest scope (D6 part 2)

Resolved: storage-account-level customer-managed key (CMK) only, covering both Blob Storage
accounts (Catalog images, Invoicing PDFs — where T1/T3/T4 data from this inventory lands at rest
as files rather than rows). Column-level encryption for T1 fields (email, name, address) was
evaluated and explicitly **not** adopted, to avoid the new `Azure.Extensions.AspNetCore.DataProtection.Keys`
NuGet dependency and `IDataProtector`/`ValueConverter` complexity for marginal benefit given Postgres
Flexible Server's own encryption-at-rest already covers those columns. Postgres itself keeps
Microsoft-managed encryption (no CMK) — see
[docs/architecture.md § Storage encryption](architecture.md#storage-encryption-customer-managed-key)
for the implementation.
