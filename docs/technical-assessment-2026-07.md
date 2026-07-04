# Technische Gesamtbewertung — OrderSphere

**Stand:** 2026-07-04 · **Basis:** `master` @ `394cf82` · **Methodik:** siehe [Methodik](#methodik) am Ende

## Executive Summary

OrderSphere ist eine Microservices-Lösung (11 Services, 51 Projekte) auf Clean Architecture + CQRS (MediatR) + DDD, orchestriert über .NET Aspire. Die Architektur ist konsequent durchgezogen: keine Schichtenverstöße, keine zirkulären Projektabhängigkeiten, kein Cross-Service-Projektzugriff. Sicherheitsrelevante Querschnittsbelange (Auth0/OIDC, Secrets über Key Vault, Rate-Limiting, CSRF, Security-Header) sind zentral in `ServiceDefaults` gebündelt und über alle Services konsistent angewendet. CI/CD deckt Build, Test, Coverage, SAST (CodeQL), Secret-Scanning (Gitleaks), Container-/Dependency-Scanning (Trivy) sowie SBOM- und Image-Signierung (Cosign/Sigstore) ab; der aktuelle `master`-Stand ist grün.

Die Schwächen liegen im Detail, nicht im Fundament: eine veraltete Aussage in `contracts/CONVENTIONS.md`, uneinheitliche Validator-Abdeckung bei Commands, fehlendes `AsNoTracking()` in einem Teil der Read-Handler, verstreute direkte `DateTime.UtcNow`-Nutzung ohne Zeit-Abstraktion, sowie eine nur teilweise belastbare automatisierte Coverage-Kennzahl. Ein bei der automatisierten Analyse zunächst kritisch wirkender Befund (121 „Compiler-Fehler" in `OrderSphere.Web`/`OrderSphere.Web.Tests`) hat sich bei Gegenprüfung als Artefakt des statischen Analyse-Tools erwiesen, nicht als echter Build-Bruch — bestätigt durch einen grünen CI-Lauf auf dem aktuellen Commit.

**Gesamtnote: 8,6 / 10** — produktionsreif mit klar benennbaren, überschaubaren Nacharbeiten (siehe [Empfehlungen](#priorisierte-empfehlungen)).

## Score-Übersicht

| # | Kategorie | Note |
|---|---|---|
| 1 | Architektur & Schichtenkonformität | 9,0 |
| 2 | Cross-Service-Kommunikation & Event-Driven-Architektur | 9,0 |
| 3 | Domain-Driven Design & Entity-Modellierung | 9,0 |
| 4 | Code-Qualität & Konventionstreue | 8,0 |
| 5 | Testabdeckung & Teststrategie | 7,0 |
| 6 | Sicherheit: Authentifizierung & Autorisierung | 9,0 |
| 7 | Sicherheit: Secrets, Daten- & Transportschutz | 9,0 |
| 8 | Observability & Betriebsreife | 9,0 |
| 9 | Resilienz | 9,0 |
| 10 | CI/CD & Supply-Chain-Sicherheit | 9,0 |
| 11 | API-Design & Schnittstellen | 8,0 |
| 12 | Statische Codeanalyse (Roslyn) | 8,0 |
| 13 | Dokumentation | 8,0 |
| | **Gesamt (arithmetisches Mittel, gleich gewichtet)** | **8,6** |

---

## 1. Architektur & Schichtenkonformität — 9/10

**Gut:**
- Solution umfasst 51 Projekte: 11 Services (Catalog, Ordering, Basket, Payment, Webhooks, UserProfile, Invoicing, Notification, Advisory, Partners + zugehörige Test-Projekte), 6 BuildingBlocks, 2 Gateways (ApiGateway, Bff), AppHost/ServiceDefaults, Frontend, 19+ Testprojekte.
- Stichprobenprüfung der `.csproj`-Referenzen (Ordering, Basket, Catalog) bestätigt die vorgeschriebene Abhängigkeitsrichtung `Api → Infrastructure → Application → Domain → BuildingBlocks.Domain` ohne Ausnahme. `Application` referenziert nirgends `Infrastructure`.
- Kein Service referenziert ein Projekt eines anderen Services direkt; Kopplung ausschließlich über HTTP-Clients, gRPC oder Service-Bus-Events.
- Dokumentierte Ausnahmen von der 4-Projekt-Regel (`Notification` als reiner Worker, `Invoicing` mit BackgroundService statt separatem Worker, `Advisory` mit zusätzlichem `Mcp.Server`-Peer-Projekt) sind in `docs/architecture.md` bzw. `src/Services/Advisory/CLAUDE.md` korrekt und mit Begründung dokumentiert — keine stillen Abweichungen.
- `dotnet-roslyn`-Analyse bestätigt **0 zirkuläre Projektabhängigkeiten** über die gesamte Solution.

**Nicht gut:**
- `contracts/CONVENTIONS.md` behauptet explizit „no gRPC services or clients exist … `contracts/proto/` … is unused. All synchronous calls are REST/HTTP" (Zeilen 8–11, 75–76). Tatsächlich existiert ein aktiver gRPC-Client: `src/Services/Basket/OrderSphere.Basket.Infrastructure/CatalogClient/GrpcCatalogClient.cs` registriert `AddGrpcClient<CatalogService.CatalogServiceClient>()` gegen den in `contracts/proto/catalog/v1/catalog.proto` definierten Dienst. Das ist eine echte Diskrepanz zwischen Dokumentation und Code, kein Interpretationsspielraum — die Datei muss korrigiert werden, da sie Entwickler aktiv in die Irre führt.

## 2. Cross-Service-Kommunikation & Event-Driven-Architektur — 9/10

**Gut:**
- Synchron: typisierte HTTP-Client-Interfaces (`ICatalogClient`, `IBasketClient`, `IOrderingClient` u. a.) in `Application/Abstractions/`, Implementierung in `Infrastructure/`; zusätzlich ein aktiver gRPC-Pfad Basket → Catalog mit M2M-Auth über denselben `ClientCredentialsTokenHandler` wie die HTTP-Clients.
- Asynchron: 11 Service-Bus-Queues mit klar dokumentierter Producer/Consumer-Zuordnung (u. a. `orders`, `payment-requests`, `payment-results`, `invoice-generation`, `webhook-events`, vier GDPR-Erasure-Queues).
- Transaktionaler Outbox/Inbox-Mechanismus konsequent umgesetzt: Outbox-Schreibvorgang in derselben DB-Transaktion wie die fachliche Änderung; Inbox dedupliziert über `EventId` vor Verarbeitung. `OutboxDispatcher` nutzt verteilte Redis-Sperre (Leader-Election), Batch-Verarbeitung (20/Zyklus), Retry mit Obergrenze (10) und Poison-Handling mit Metrik (`OutboxMetrics.Poison`).
- Checkout-Saga mit Kompensationslogik (Bestandsreservierung mit TTL, Freigabe im Fehlerfall) sowie Redis-gestützte Idempotenz für Checkout-Anfragen (`RedisCheckoutIdempotencyStore`, deterministische CorrelationId, 30-Minuten-TTL).
- Trace-Kontext (W3C `traceparent`) wird über die asynchrone Grenze hinweg im Outbox-Datensatz mitgeführt und beim Dispatch wiederhergestellt — durchgängige verteilte Nachverfolgbarkeit auch über Messaging hinweg.

**Nicht gut:**
- Die Konsistenz der Queue-Konfiguration (z. B. `MaxDeliveryCount`) ist nur in `AppHost.cs` und `docs/architecture.md` gepflegt; ein Drift zwischen beiden wurde nicht geprüft, ist aber ein struktureller Risikofaktor bei zwei Wahrheitsquellen für dieselbe Information.

## 3. Domain-Driven Design & Entity-Modellierung — 9/10

**Gut:**
- Entitäten mit echtem Verhalten statt anämischer Datencontainer: `Product.RemoveFromStock()` liefert `Result`, löst Domain-Events aus; `Order` (Ordering.Domain) ist ein event-sourced Aggregate mit Apply-Fold-Rekonstruktion aus einem Event-Stream und optimistischer Nebenläufigkeitskontrolle über einen zusammengesetzten Schlüssel `(StreamId, Version)`.
- Value Objects (`Money`, `Address`, `Quantity`), Strongly-Typed IDs (`ProductId`, `OrderId` etc.) statt nackter `Guid`s durchgängig verwendet.
- `AuditableEntity<TId>` als einheitliche Basis für Audit-Felder, Soft-Delete-Flag und Domain-Event-Sammlung; das globale `HasQueryFilter(x => !x.IsDeleted)` ist in den EF-Configurations aller geprüften Aggregate (Product, Category, Cart, Order, Invoice u. a.) vorhanden — keine Handler wiederholen den Filter manuell, wie in `CLAUDE.md` gefordert.

**Nicht gut:**
- Keine wesentlichen Mängel identifiziert; die einzige nennenswerte Einschränkung ist die unten (Kategorie 4) beschriebene fehlende Zeit-Abstraktion, die auch Domain-nahe Audit-Logik betrifft.

## 4. Code-Qualität & Konventionstreue — 8/10

**Gut:**
- `Result<T>`-Konformität ist über alle 84 geprüften Command-/Query-Handler hinweg vollständig: keine Exceptions für fachliche Validierung, kein `.Result`/`.Wait()`/`.GetAwaiter().GetResult()` in Application/Domain-Code.
- `<Nullable>enable</Nullable>` gilt solution-weit (`Directory.Build.props`); keine themenfremde Unterdrückung von Nullable-Warnungen außer der gerechtfertigten Razor-generierten `CS8669`.
- DTOs sind zu 100 % `record`-Typen, Entitäten zu 100 % `class`-Typen (Stichprobe: 20+ DTOs, 15+ Entitäten) — Konvention aus `CLAUDE.md` wird eingehalten.
- Zentrales Package-Management (`Directory.Packages.props`, 102 Einträge) ohne Versionskonflikte; alle Kernpakete aktuell (MediatR 14.1.0, EF Core 10.0.9, FluentValidation 12.1.1, Aspire 13.4.6). Eine Sicherheitslücke (MessagePack, GHSA-hv8m-jj95-wg3x) ist mit fixierter Patch-Version dokumentiert behoben.
- Keine TODO/FIXME/HACK-Marker, keine `[Obsolete]`-Leichen im produktiven Code.

**Nicht gut:**
- Validator-Abdeckung bei Commands liegt bei 73 % (24 von 33): 0 % bei Payment, 50 % bei Partners, je 60 % bei Basket und Ordering. Das widerspricht nicht direkt einer expliziten Konvention, ist aber eine Inkonsistenz innerhalb des Feature-Musters, die zu unbemerkt fehlender Eingabevalidierung führen kann.
- `TreatWarningsAsErrors` ist solution-weit auf `false` gesetzt (mit Kommentar als bewusst temporär markiert) — Nullable- und Analyzer-Warnungen können sich unbemerkt anhäufen, solange dieses Gate nicht wieder aktiviert wird.
- Roslyn-Statik findet echte, aber Low/Medium-Severity-Muster: **~49 Read-Query-Handler ohne `AsNoTracking()`** (u. a. `AddToCartCommandHandler`, `GetCartQueryHandler`, mehrere Catalog-Endpoints) — unnötiger EF-Core-Change-Tracking-Overhead bei reinen Lesevorgängen; **~36 Stellen mit direktem `DateTime.UtcNow`** ohne injizierbare Zeit-Abstraktion, konzentriert in Outbox/Inbox/Audit-Infrastruktur — macht zeitabhängige Logik dort schwerer deterministisch testbar.
- Vier bewusste leere `catch`-Blöcke (`Bff/Program.cs:141`, `DistributedLockExtensions.cs:132`, `SemanticCacheChatClient.cs:75`, `WebhookEventProcessor.cs:176`) sind zwar inhaltlich gerechtfertigte Fail-Open-Muster mit erklärendem Kommentar, protokollieren den abgefangenen Fehler aber nicht einmal auf Debug-Level — im Störungsfall ist das Verhalten korrekt, aber schwerer nachvollziehbar.

## 5. Testabdeckung & Teststrategie — 7/10

**Gut:**
- 19 Testprojekte, 168 Testklassen; Domain-, Application- und Integrationsebene sind vertreten (u. a. `OrderSphere.Domain.Tests`, `OrderSphere.Ordering.Checkout.Tests`, `OrderSphere.IntegrationTests`, `OrderSphere.ContainerTests`).
- Moderner Test-Stack: xUnit 2.9.3, FluentAssertions 8.10.0, NSubstitute 5.3.0, WebApplicationFactory, Testcontainers-taugliche Integrationstests.
- Stichproben (`CreateReviewCommandHandlerTests`, `CartTests`) zeigen aussagekräftige Assertions statt Trivialtests.

**Nicht gut:**
- Die automatisierte, namensbasierte Coverage-Heuristik (Type → TypeTests) kommt auf lediglich **10 % (131 von 1236 Typen)**. Dieser Wert ist mit Vorsicht zu lesen — DTOs, EF-Migrationen, EntityConfigurations und Model-Snapshots benötigen keine 1:1-Testklasse und blähen den Nenner auf —, zeigt aber, dass eine belastbare Coverage-Kennzahl (Line/Branch-Coverage aus dem CI-Report) bislang nicht als Qualitätsgate im Bericht sichtbar gemacht wird.
- Payment, Partners und Invoicing haben spürbar dünnere Integrationstestabdeckung als Ordering; bei Payment fehlt zusätzlich jeglicher Command-Validator (siehe Kategorie 4), was die fehlende Testabsicherung dort verschärft.
- Der zunächst kritisch wirkende Fund „121 Compiler-Fehler in `OrderSphere.Web`/`OrderSphere.Web.Tests`" (Roslyn-MCP-Diagnose) betraf ausschließlich fehlende Razor-generierte Partial-Klassen (`ProductCard`, `CartDrawer`, `StarRating`, `OrderSummary`, `AppErrorBoundary`) und eine nicht aufgelöste bUnit-Erweiterungsmethode (`RenderTreeBuilder.Add`). Das ist ein Artefakt der statischen Analyse-Workspace (kein vollständiger Razor-Source-Generator-Lauf, bUnit nicht im Analyzer-Kontext), **kein echter Build-Fehler** — bestätigt durch einen erfolgreichen `CI`-Lauf auf dem aktuellen `master`-Commit (`394cf82`, Status „success"). Dieser Punkt wird hier dokumentiert, weil er in einer rein automatisierten Analyse leicht fälschlich als kritischer Blocker gemeldet würde.

## 6. Sicherheit: Authentifizierung & Autorisierung — 9/10

**Gut:**
- JWT-Validierung zentral über `AddOrderSphereJwtAuth<TBuilder>()` in `ServiceDefaults`, von allen 10 API-Services genutzt: `ValidateAudience`, `ValidateIssuer`, `ValidateLifetime`, `ValidateIssuerSigningKey` aktiv, `RequireHttpsMetadata` an die Umgebung gekoppelt, `MapInboundClaims = false` zum Erhalt des Auth0-Claim-Namespace.
- BFF-Pattern mit eigenem OpenIdConnect-Cookie-Flow: Session-Verschlüsselung über ASP.NET Data Protection + Redis, CSRF-Token-Austausch (`X-XSRF-TOKEN`), sichere Cookie-Flags (`HttpOnly`, `SameSite=Strict`, `Secure` in Produktion), Backchannel-Logout.
- Rollenbasierte Autorisierung konsistent per fluenter `RequireAuthorization()`-Policy statt verstreuter `[Authorize]`-Attribute; B2B-Zugriff über gehashte API-Keys (SHA-256, kein Klartext gespeichert) mit eigenem Policy-Scheme.
- Service-zu-Service-Auth über OAuth2 Client-Credentials mit Token-Caching (Erneuerung 30 Sekunden vor Ablauf, `SemaphoreSlim` gegen Doppel-Anfragen) statt statischer Shared Secrets im Klartext.
- Strukturiertes Security-Audit-Logging (`SecurityAuditLogger`) für Login-Fehlschläge, Token-Validierungsfehler, CSRF-Verstöße und Autorisierungs-Ablehnungen, verknüpft mit Trace-IDs.

**Nicht gut:**
- Die 8-Stunden-Sitzungsdauer der BFF-Session (`ExpireTimeSpan`) ist ein Konfigurationswert, der je nach Risikoprofil des Einsatzumfelds zu lang sein kann — keine Fehlkonfiguration, aber ein Punkt für eine bewusste Risikoentscheidung vor Produktivbetrieb.
- Rotationsintervalle für Partner-API-Keys sind nicht erkennbar dokumentiert oder erzwungen.

## 7. Sicherheit: Secrets, Daten- & Transportschutz — 9/10

**Gut:**
- Keine Klartext-Secrets im Repository gefunden. Der einzige „Fund" (Auth0 `ClientId` in `src/Gateways/OrderSphere.Bff/appsettings.json`) ist laut OAuth2/OIDC-Spezifikation ein öffentlicher Bezeichner, kein Geheimnis; das zugehörige `ClientSecret` liegt korrekt außerhalb des Repos.
- Sauber getrennte Secret-Strategie: User Secrets lokal, Azure Key Vault (mit Purge Protection, Grundlage für CMK) in Produktion, Bereitstellung über `azd`/GitHub-Actions-Secrets ohne Klartext im Log.
- Verschlüsselung: Storage-Accounts mit Customer-Managed Keys und System-Assigned Identity; HTTPS/HSTS erzwungen in Produktion; restriktive Content-Security-Policy für die Blazor-WASM-App (`script-src 'self' 'wasm-unsafe-eval'`, keine externen Ressourcen).
- GDPR-Recht auf Löschung technisch umgesetzt: `CustomerErasureRequestedIntegrationEvent` fächert über vier dedizierte Queues an Ordering, Payment, Invoicing und Advisory auf; Advisory führt einen echten Hard-Delete der Konversationsdaten durch (Artikel-17-Konformität).

**Nicht gut:**
- Keine kritischen Befunde. Einzige Beobachtung: Die Aufbewahrungsfrist für Audit-/Security-Logs ist nicht explizit als Richtlinie dokumentiert (Cleanup-Jobs existieren technisch, TTL-Werte sollten für einen Compliance-Nachweis schriftlich festgehalten werden).

## 8. Observability & Betriebsreife — 9/10

**Gut:**
- Vollständiger OpenTelemetry-Stack: strukturierte Logs, verteiltes Tracing (u. a. eigene `ActivitySource`s für `OrderSphere.EventBus` und MediatR-Handler-Aufrufe via `LoggingBehavior`), Metriken (ASP.NET Core, HTTP-Client, Runtime, plus eigener `OrderSphere`-Meter für Outbox-Publish/Poison-Zähler).
- Health-Checks konsequent nach Liveness/Readiness getrennt (`/alive` vs. `/health`), inklusive Datenbank-, Redis- und Service-Bus-Prüfungen je nach Abhängigkeiten des Service.
- Dualer Exporter-Pfad (OTLP für lokales Aspire-Dashboard, Azure Monitor/Application Insights für Produktion) ohne Codeänderung zwischen den Umgebungen.
- Trace-Sampling ist konfigurierbar (`OpenTelemetry:TracesSampleRatio`) mit `ParentBasedSampler`, respektiert also vorgelagerte Sampling-Entscheidungen.

**Nicht gut:**
- Der Standard-Sampling-Wert steht (laut Empfehlung des Security-Reviews) noch auf 1.0 (100 %) statt eines produktionsnäheren Werts — bei hohem Durchsatz ein Kostenfaktor, kein funktionales Risiko.

## 9. Resilienz — 9/10

**Gut:**
- Globale HTTP-Resilienz über `AddStandardResilienceHandler()` (Microsoft.Extensions.Http.Resilience) für alle HTTP-Clients: Retry mit Backoff, Circuit Breaker, Timeout; nur idempotente GETs werden retried.
- EF Core mit `DisableRetry = false` für alle 11 Datenbanken (transiente Fehler werden über Aspire-Npgsql-Integration automatisch abgefangen).
- Outbox-Dispatcher mit verteilter Sperre (Redis, TTL 30s), begrenztem Retry (max. 10) und Poison-Queue-Pfad statt Endlosschleifen bei dauerhaft fehlerhaften Nachrichten.
- Distributed-Lock-Implementierung mit Lease-Verlängerung und sauberem Dispose-Pfad (siehe auch die dortige, gerechtfertigte leere `catch`-Klausel in Kategorie 4).

**Nicht gut:**
- Keine wesentlichen Lücken identifiziert. Eine vollständige Bewertung der Circuit-Breaker-Schwellenwerte unter Lastbedingungen wurde im Rahmen dieser Analyse nicht durchgeführt (erfordert Lasttests, kein Code-Review-Befund).

## 10. CI/CD & Supply-Chain-Sicherheit — 9/10

**Gut:**
- 6 GitHub-Actions-Workflows: `ci.yml` (Build, Test mit Coverage-Gate, Testreport), `security.yml` (Gitleaks + Trivy, SARIF-Upload), `codeql.yml` (SAST), `dependency-review.yml`, `vuln-scan.yml` (transitive Abhängigkeiten), `release-deploy.yml` (versionierte Releases inkl. SBOM und `azd provision`/`azd deploy`).
- Aktueller `master`-Stand (`394cf82`) ist über alle geprüften Workflows (CI, CodeQL, Security) grün.
- Least-Privilege-Permissions, Runner-Hardening (`step-security/harden-runner`), gepinnte Action-Versionen per SHA, OIDC-föderierte Azure-Anmeldung statt statischer Service-Principal-Schlüssel.
- Keyless Image-Signierung (Cosign/Sigstore über GitHub OIDC) und CycloneDX-SBOM-Erzeugung für Supply-Chain-Nachvollziehbarkeit.

**Nicht gut:**
- Keine wesentlichen Lücken identifiziert.

## 11. API-Design & Schnittstellen — 8/10

**Gut:**
- Konsistente URL-basierte API-Versionierung (`Asp.Versioning`, `/api/v1/...`), dokumentiertes Vorgehen für Breaking Changes (`/api/v2/` parallel statt Migration in-place).
- Mehrstufiges, Redis-gestütztes Rate-Limiting: global, pro authentifiziertem Nutzer, pro API-Key-Tier (Partner Standard/Premium), pro anonymer IP — mit sauberer 429-Antwort.
- Alle Services ausschließlich über den YARP-API-Gateway von außen erreichbar; kein direkter Browser-Zugriff auf interne Service-Endpunkte.

**Nicht gut:**
- `AllowedHosts: "*"` auf dem BFF ist im Kontext des Same-Origin-BFF-Patterns unkritisch, sollte aber als bewusste, dokumentierte Ausnahme und nicht als generischer Konfigurationswert im Code stehen, damit sie bei künftigen Refactorings nicht versehentlich auf einen tatsächlich CORS-exponierten Dienst übertragen wird.

## 12. Statische Codeanalyse (Roslyn) — 8/10

**Gut:**
- **0 zirkuläre Projektabhängigkeiten** über die gesamte Solution.
- **24 tote Symbole** (ungenutzte Typen/Methoden) bei 1236 geprüften Typen — ein sehr niedriger Wert für ein Projekt dieser Größe; die meisten Treffer sind ohnehin EF-Migration-Snapshots und generierte `ModelSnapshot`-Klassen, die vom Werkzeug fälschlich als „tot" markiert werden (sie werden von EF Core reflektiv geladen, nicht direkt referenziert).
- Von 982 initial gemeldeten Antipattern-Treffern (200 im Detail ausgewertet) sind nach manueller Prüfung mehrere Kategorien vollständig oder größtenteils entkräftet:
  - Alle 16 `#pragma warning disable`-Funde liegen in generiertem gRPC-Proto-Code (`Catalog.cs`, `CatalogGrpc.cs`), nicht in handgeschriebenem Code.
  - Alle 3 „Sync-over-async"-Funde sind Fehlalarme: zwei sind Zugriffe auf `Polly.OutcomeArguments.Result` (keine Task-Blockierung), einer ist `FunctionResultContent.Result` (Microsoft.Extensions.AI-Property).
  - Der eine „`new HttpClient()`"-Fund liegt im Blazor-WASM-`Program.cs` — dort ist ein einzelner scope-gebundener `HttpClient` mit fester `BaseAddress` das im Blazor-Template vorgesehene Muster, nicht das klassische serverseitige Socket-Exhaustion-Risiko.
  - Von 24 als „leerer catch-Block" markierten Stellen sind ca. 20 `catch (OperationCanceledException) { }` in Worker-Shutdown-Pfaden (idiomatisch) und die verbleibenden 4 geprüften Fälle sind bewusste, kommentierte Fail-Open-Muster (siehe Kategorie 4) — keine stillen Fehlerverschluckungen ohne Begründung.

**Nicht gut:**
- Zwei Kategorien bleiben nach Prüfung als echte, wenn auch geringfügige Befunde bestehen: fehlendes `AsNoTracking()` (49 Fundstellen) und direkte `DateTime.UtcNow`-Nutzung ohne Abstraktion (36 Fundstellen) — siehe Kategorie 4 für Details und betroffene Bereiche.
- Die vier real bestätigten leeren `catch`-Blöcke protokollieren den abgefangenen Fehler nicht (siehe Kategorie 4).

## 13. Dokumentation — 8/10

**Gut:**
- `docs/architecture.md` ist umfassend und deckungsgleich mit dem tatsächlichen Code: korrektes Service-Inventar, korrekte Ausnahmen von der 4-Projekt-Regel, Event-Sourcing-Beschreibung für `Order`, Queue-Inventar, M2M-Auth-Modell, SBOM/Signierungs-Pipeline.
- `docs/ui-conventions.md` stimmt mit der tatsächlichen Implementierung überein (6 Marken, Typografie-Tokens, i18n-Struktur in `Resources/AppStrings.resx`).

**Nicht gut:**
- `contracts/CONVENTIONS.md` enthält die oben (Kategorie 1) beschriebene, faktisch falsche Aussage zu gRPC — das ist der einzige, aber eindeutige Dokumentations-Code-Drift, der in dieser Analyse gefunden wurde.

---

## Priorisierte Empfehlungen

| Priorität | Befund | Empfehlung |
|---|---|---|
| P1 | `contracts/CONVENTIONS.md` behauptet fälschlich, gRPC sei ungenutzt, obwohl Basket→Catalog es aktiv nutzt | Abschnitt korrigieren, `GrpcCatalogClient` als aktive Konvention dokumentieren |
| P1 | 0 % Validator-Abdeckung bei Payment-Commands, 50 % bei Partners | Fehlende `AbstractValidator<T>`-Klassen ergänzen, insbesondere für Payment als geldbewegenden Service |
| P2 | `TreatWarningsAsErrors=false` solution-weit (als „temporär" markiert) | Zeitpunkt/Kriterium für Reaktivierung festlegen, sonst akkumulieren stillschweigend Nullable-/Analyzer-Warnungen |
| P2 | ~49 Read-Handler ohne `AsNoTracking()` | In den Application-weiten EF-Core-Query-Konventionen verbindlich machen und schrittweise nachziehen |
| P2 | Verstreute `DateTime.UtcNow`-Nutzung ohne Zeit-Abstraktion (Outbox/Inbox/Audit) | `TimeProvider` (seit .NET 8 im BCL) als injizierbare Abstraktion einführen, beginnend in `BuildingBlocks` |
| P2 | Vier bewusste leere `catch`-Blöcke ohne Log-Eintrag | Mindestens `LogDebug` mit der Exception ergänzen, um Fail-Open-Fälle im Betrieb nachvollziehbar zu machen |
| P2 | OpenTelemetry-Trace-Sampling auf 100 % | Vor Produktivlast auf einen reduzierten Wert (z. B. 10 %) einstellen, nach Lasttest-Ergebnis |
| P3 | Testabdeckung bei Payment/Partners/Invoicing dünner als bei Ordering | Integrationstests dort gezielt nachziehen, insbesondere für Payment angesichts der fehlenden Validatoren |
| P3 | Partner-API-Key-Rotation nicht dokumentiert | Rotationsintervall festlegen und in `docs/architecture.md` oder einer Betriebsrichtlinie festhalten |
| P3 | BFF-Session-Dauer 8 Stunden | Gegen das Risikoprofil des Zielbetriebs prüfen, ggf. reduzieren |

## Produktionsreife-Einschätzung

**Produktionsreif mit überschaubaren Nacharbeiten.** Architektur, Sicherheitsmodell, Observability, Resilienz und CI/CD-Pipeline erfüllen die Anforderungen an ein produktives Enterprise-System ohne strukturelle Nachbesserung. Kein Befund in dieser Analyse erfordert einen Architektur- oder Sicherheitsumbau. Die offenen Punkte sind lokal begrenzte Konventions- und Abdeckungslücken (P1/P2 oben), die vor einem produktiven Go-Live abgearbeitet werden sollten, aber keinen Aufschub der Gesamteinführung rechtfertigen.

---

## Methodik

Die Bewertung stützt sich auf vier unabhängige, rein lesende Untersuchungen:

1. Manuelle Code- und Strukturprüfung (Architektur, Service-Inventar, Schichtenreferenzen, Dokumentationsabgleich).
2. Manuelle Code- und Strukturprüfung (Konventionstreue: `Result<T>`, Nullable, DTO/Entity-Typen, Validatoren, Testprojekt-Inventar, NuGet-Hygiene).
3. Manuelle Code- und Strukturprüfung (Security: Auth, Secrets, Observability, Resilienz, CI/CD, CORS/Rate-Limiting, Outbox/Inbox).
4. Automatisierte statische Analyse via Roslyn-MCP-Tooling (Antipattern-Erkennung, zirkuläre Abhängigkeiten, toter Code, Compiler-Diagnostik, Coverage-Heuristik), anschließend manuell gegengeprüft.

Alle automatisiert generierten Befunde (insbesondere aus Schritt 4) wurden gegen den tatsächlichen Quellcode und — im Fall der gemeldeten Compiler-Fehler — gegen einen echten CI-Lauf verifiziert, bevor sie in diesen Bericht übernommen wurden. Reine Tool-Artefakte (Razor-/bUnit-Generatoren außerhalb der Analyse-Workspace, Property-Namenskollisionen wie `.Result`) wurden entsprechend gekennzeichnet und nicht als Defekt gewertet.

Nicht Teil dieser Analyse: Lasttests, Penetrationstests, tatsächliche Laufzeit-Coverage-Messung (nur die namensbasierte Heuristik wurde ausgewertet), Review des Frontend-UI/UX jenseits der Konventionsprüfung.

---

## Anhang: Vorgeschlagene Folge-Epics (Backlog)

**Status: vorgeschlagen, noch nicht in Stories/Sub-Issues aufgebrochen.** Festgehalten am 2026-07-04
zur späteren Wiederaufnahme. Nummerierung knüpft an die bestehenden Workstreams A–F im
GitHub-Projekt an (alle A–F sowie die separat geführten WS-5/6/7 sind zu diesem Zeitpunkt
abgeschlossen).

| # | Epic | Kurzbeschreibung | Herkunft | Größe |
|---|---|---|---|---|
| G | Dokumentations- & Konventions-Korrekturen | `contracts/CONVENTIONS.md`-gRPC-Fix, Queue-Konfig-Drift (AppHost.cs vs. docs/architecture.md), Audit-Log-Retention schriftlich fixieren | P1 (Kat. 1/13), Nebenpunkt Kat. 2/7 | XS–S |
| H | Validator- & Warnungs-Härtung | Fehlende `AbstractValidator<T>` bei Payment (0 %), Partners (50 %), Basket/Ordering (je 60 %); Reaktivierungsplan für `TreatWarningsAsErrors` | P1/P2 (Kat. 4) | S–M |
| I | EF-Core-Query- & Zeit-Abstraktion | `AsNoTracking()` in ~49 Read-Handlern nachziehen; `TimeProvider` als injizierbare Zeitquelle einführen (Outbox/Inbox/Audit) | P2 (Kat. 4/12) | M |
| J | Fehlerbehandlungs-Transparenz | Die 4 bewussten leeren `catch`-Blöcke um `LogDebug`/`LogWarning` ergänzen | P2 (Kat. 4) | XS |
| K | Testabdeckung vertiefen | Integrationstests für Payment/Partners/Invoicing auf Ordering-Niveau heben; echtes Line/Branch-Coverage-Reporting statt Heuristik | P3 (Kat. 5) | M–L |
| L | Last- & Resilienztests | Lasttests zur Circuit-Breaker-Schwellenwert-Validierung; Chaos-/Fault-Injection-Tests | Neue Idee (Kat. 9, explizit nicht Teil dieser Analyse) | M |
| M | Security-Vertiefung Runde 2 | BFF-Session-Dauer prüfen; Partner-API-Key-Rotation dokumentieren + erzwingen; externer Penetrationstest | P3 (Kat. 6), Neue Idee (Pentest) | M |
| N | Observability-Produktionstuning | Trace-Sampling von 100 % senken (nach Lasttest aus L); Kosten-/Alerting-Review | P2 (Kat. 8) | S |
| O | Betriebs-Runbooks & DR | Disaster-Recovery-Runbook, Backup-Restore-Drill, Incident-Response-/On-Call-Prozess | Neue Idee | M |
| P | Dependency- & Patch-Automatisierung | Automatisiertes Dependency-Update (Dependabot/Renovate) ergänzend zu Trivy/CodeQL/Gitleaks | Neue Idee | S |
| Q | API-Lifecycle-Policy | Formale Deprecation-/Sunset-Policy für `/api/v1` → `/api/v2` | Neue Idee | S |
| R | FinOps / Kostentransparenz | Azure Cost-Alerts, Ressourcen-Tagging pro Service | Neue Idee | S |

Epics G–M decken alle zehn Einträge der Tabelle [Priorisierte Empfehlungen](#priorisierte-empfehlungen)
sowie die beiden zusätzlichen "Nicht gut"-Punkte aus Kategorie 2 (Queue-Konfig-Drift) und Kategorie 7
(Log-Retention) vollständig ab. Epics L, O, P, Q, R sind keine Berichtsbefunde, sondern zusätzliche
Produktionsreife-Themen außerhalb des Analyseumfangs (siehe Methodik oben).

Nächster Schritt bei Wiederaufnahme: Auswahl der umzusetzenden Epics, danach Aufbrechen in
Stories/Sub-Issues (Titel, Beschreibung, Labels `workstream:G`.. usw., Größe) im Format der
bestehenden GitHub-Projekt-Epics A–F.
