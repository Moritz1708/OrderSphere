# GitHub Projects Backlog für OrderSphere

> Basierend auf der Technischen Gesamtbewertung 2026-07-04 (`technical-assessment-2026-07.md`)  
> **Status**: Alle Items im Backlog, noch nicht Sprint-zugeordnet  
> **Datum erstellt**: 2026-07-04  
> **Zielzustand**: Story Points schätzen → First Sprint planen

---

## Übersicht

Dieses Backlog enthält alle Arbeitspakete aus der technischen Bewertung, strukturiert nach **Priority** (P1/P2/P3) und organisiert nach **Epics** (G–R).

- **P1 (Critical)**: Vor Produktivbetrieb erforderlich (Dokumentation + Validierungslücken)
- **P2 (High)**: In nächster/übernächster Sprint erledigen (Code-Qualität, EF-Core, Observability)
- **P3 (Medium)**: Backlog für später (Testabdeckung, Security-Tiefgang, FinOps)

**Total**: 30 Items (davon 12 als Epic-Überblick, 18 als granulare Tasks)

---

## Epic G: Dokumentations- & Konventions-Korrektionen

**Priority**: P1  
**Größe (geschätzt)**: S (Small, 5–8 Punkte)  
**Kategorie**: Docs, Konvention  
**Begründung**: Code und Doku sind aus dem Sync; führt zu Verwirrung für neue Entwickler

### Sub-Tasks:

#### G.1: `contracts/CONVENTIONS.md` — gRPC-Section korrigieren
**Story Points**: 2  
**Labels**: `type:docs`, `priority:p1`, `epic:G`  
**Beschreibung**:
- Aktuelle Aussage (Z. 8–11, 75–76): „no gRPC services exist"
- Tatsächlich: `Basket → Catalog` nutzt aktiv `GrpcCatalogClient`
- **Action**: Abschnitt umschreiben, `GrpcCatalogClient` als offizielle Konvention dokumentieren, gRPC-Pfad ins Service-Übersichtstabel aufnehmen
- **Link**: `src/Services/Basket/OrderSphere.Basket.Infrastructure/CatalogClient/GrpcCatalogClient.cs`

#### G.2: Queue-Konfiguration synchronisieren (AppHost.cs ↔ docs/architecture.md)
**Story Points**: 3  
**Labels**: `type:docs`, `priority:p1`, `epic:G`  
**Beschreibung**:
- Queue-Config (insb. `MaxDeliveryCount`) wird in zwei Orten gepflegt: `AppHost.cs` + `docs/architecture.md`
- **Action**: Automatisierten Drift-Check etablieren oder eine Quelle als Source-of-Truth definieren (z. B. Aspire-Code mit Doku-Extraktion)
- **Wert**: Verhindert Silent-Failures bei Queue-Reconfig

#### G.3: Audit-Log-Retention-Policy schriftlich dokumentieren
**Story Points**: 2  
**Labels**: `type:docs`, `priority:p1`, `epic:G`, `security`  
**Beschreibung**:
- TTL-Werte für Audit/Security-Logs existieren technisch, sind aber nicht als Richtlinie dokumentiert
- **Action**: Explizite Retention-Policy in `docs/` oder separate `SECURITY-POLICY.md` schreiben (z. B. „Security-Logs 90d, Audit-Logs 1a")
- **Wert**: Compliance-Nachweis (DSGVO, SOC2), Betriebsrichtlinie statt Code-Implizit

---

## Epic H: Validator- & Warnungs-Härtung

**Priority**: P1/P2  
**Größe (geschätzt)**: M (Medium, 8–13 Punkte)  
**Kategorie**: Code Quality  
**Begründung**: 27% der Commands haben keinen Validator; `TreatWarningsAsErrors=false` versteckt Nullable-Probleme

### Sub-Tasks:

#### H.1: `AbstractValidator<T>` für Payment-Commands implementieren
**Story Points**: 5  
**Labels**: `type:feat`, `priority:p1`, `epic:H`, `service:Payment`  
**Beschreibung**:
- **Fund**: Payment-Service hat 0 % Validator-Abdeckung (alle 3 Commands ohne Validatoren)
- **Kritikalität**: Geldbewegungen — keine Eingabevalidierung ist ein Sicherheits-/Datenleck
- **Action**: Validator-Klassen für `ChargePaymentCommand`, `RefundPaymentCommand`, `UpdatePaymentStatusCommand` erstellen
- **Pattern**: S. `src/Services/Ordering/OrderSphere.Ordering.Application/Features/<Aggregate>/<UseCase>/` als Template
- **Test**: Auch Unit-Tests für Validator schreiben (HappyPath + Fail-Cases)

#### H.2: Partner-Commands: Fehlende Validatoren ergänzen
**Story Points**: 3  
**Labels**: `type:feat`, `priority:p2`, `epic:H`, `service:Partners`  
**Beschreibung**:
- **Fund**: Partners-Service nur 50% Validator-Abdeckung
- **Action**: Audit aller Partner-Commands durchführen, fehlende Validatoren implementieren
- **Ziel**: 100% Abdeckung pro Service

#### H.3: `TreatWarningsAsErrors` wieder auf `true` aktivieren
**Story Points**: 5  
**Labels**: `type:refactor`, `priority:p2`, `epic:H`, `codebase`  
**Beschreibung**:
- **Fund**: `Directory.Build.props` setzt `TreatWarningsAsErrors=false` (mit Kommentar als „temporär")
- **Problem**: Nullable-Warnungen und Analyzer-Warnungen akkumulieren unbemerkt
- **Action**:
  1. Aktuell alle Nullable-Warnungen durch echte Type-Fixes beheben (nicht `#pragma`)
  2. `TreatWarningsAsErrors=true` wieder aktivieren
  3. CI-Gate Schritt durchlaufen (sollte grün sein)
- **Nutzen**: Zwingt zu disziplinierten Nullable-Declarations

---

## Epic I: EF-Core-Query- & Zeit-Abstraktion

**Priority**: P2  
**Größe (geschätzt)**: M–L (13–21 Punkte)  
**Kategorie**: Performance, Code Quality  
**Begründung**: ~49 Read-Handler ohne `AsNoTracking()` verursachen unnötige Change-Tracking-Kosten; direkte `DateTime.UtcNow` erschwert Tests

### Sub-Tasks:

#### I.1: `AsNoTracking()` in Read-Handlern durchzusetzen
**Story Points**: 8  
**Labels**: `type:refactor`, `priority:p2`, `epic:I`, `performance`  
**Beschreibung**:
- **Fund**: ~49 Query-Handler (u. a. `GetCartQueryHandler`, Catalog-Read-Endpoints) ohne `AsNoTracking()`
- **Impact**: Unnötige EF-Core-Change-Tracking-Overhead für reine Lesevorgänge
- **Action**:
  1. EF-Core-Query-Konventionen in `Infrastructure/QueryConventions.cs` festlegen: `queryable.AsNoTracking()` global für alle Queries
  2. Oder: Pro Read-Handler manuell durchgehen und `AsNoTracking()` hinzufügen
  3. Roslyn-Analyzer/Linter rule schreiben, um Regression zu verhindern
- **Test**: Lasttests vor/nach zum Perf-Improvement messen

#### I.2: `TimeProvider` (Abstraktion für `DateTime.UtcNow`) einführen
**Story Points**: 8  
**Labels**: `type:feat`, `priority:p2`, `epic:I`, `testability`  
**Beschreibung**:
- **Fund**: ~36 Stellen mit direktem `DateTime.UtcNow`, konzentriert in Outbox/Inbox/Audit-Code
- **Problem**: Zeitabhängige Logik ist schwer deterministisch testbar
- **Action**:
  1. `BuildingBlocks` um `ITimeProvider` Service erweitern (oder `TimeProvider` aus .NET 8+ BCL verwenden)
  2. `DateTime.UtcNow` durch Dependency Injection ersetzen:
     ```csharp
     // Statt: var now = DateTime.UtcNow;
     // Neu: var now = _timeProvider.GetUtcNow();
     ```
  3. Alle Outbox/Inbox/Audit-Zeitzugriffe auf die abstrahierte API migrieren
  4. Unit-Tests mit `FakeTimeProvider` schreiben
- **Referenz**: Microsoft.Extensions.TimeProvider (NuGet)

---

## Epic J: Fehlerbehandlungs-Transparenz

**Priority**: P2  
**Größe (geschätzt)**: XS (1–3 Punkte)  
**Kategorie**: Code Quality, Observability  
**Begründung**: 4 bewusste leere `catch`-Blöcke (Fail-Open-Muster) loggen Fehler nicht → erschwert Debugging

### Sub-Tasks:

#### J.1: Leere `catch`-Blöcke mit Debug-Logging ausstatten
**Story Points**: 2  
**Labels**: `type:fix`, `priority:p2`, `epic:J`  
**Beschreibung**:
- **Fund**: 4 Stellen mit bewussten leeren `catch`-Blöcken:
  - `Bff/Program.cs:141`
  - `DistributedLockExtensions.cs:132`
  - `SemanticCacheChatClient.cs:75`
  - `WebhookEventProcessor.cs:176`
- **Kontext**: Diese sind idiomatisch (Fail-Open-Muster), aber nur mit Kommentar dokumentiert
- **Action**: Jede Stelle um mindestens `_logger.LogDebug("Fail-open: {Exception}", ex);` ergänzen
- **Nutzen**: Betriebsteam kann im Störungsfall nachvollziehen, warum eine Komponente failover ist

---

## Epic K: Testabdeckung vertiefen

**Priority**: P3  
**Größe (geschätzt)**: M–L (13–21 Punkte)  
**Kategorie**: Testing, Quality Assurance  
**Begründung**: Payment/Partners/Invoicing haben dünnere Integration-Testabdeckung; Payment zusätzlich ohne Validatoren

### Sub-Tasks:

#### K.1: Payment-Service: Integration-Tests auf Ordering-Niveau heben
**Story Points**: 8  
**Labels**: `type:test`, `priority:p3`, `epic:K`, `service:Payment`  
**Beschreibung**:
- **Fund**: Payment-Integrationstests viel dünner als Ordering; + fehlende Validatoren verschärft das
- **Action**:
  1. Test-Szenarien identifizieren: Happy Path (Charge), Decline, Refund, Idempotenz, Webhook-Verification
  2. WebApplicationFactory + Testcontainers (Real DB) einrichten
  3. Tests schreiben (min. 5–10 Integrationstests)
- **Abhängigkeit**: Wartet auf H.1 (Validatoren)

#### K.2: Partners & Invoicing: Integrationstests nachziehen
**Story Points**: 5  
**Labels**: `type:test`, `priority:p3`, `epic:K`, `service:Partners,Invoicing`  
**Beschreibung**:
- **Action**: Ähnlich wie K.1, aber für Partners (API-Key-Flows) und Invoicing (PDF-Generation, Retention)
- **Min. Ziel**: Keine Critical-Path-Handler ohne Integrationstest

#### K.3: Line/Branch-Coverage-Reporting etablieren
**Story Points**: 3  
**Labels**: `type:chore`, `priority:p3`, `epic:K`, `ci/cd`  
**Beschreibung**:
- **Fund**: Coverage-Heuristik (Namen) ergibt nur 10%, weil DTOs/EF-Migrations mitzählen
- **Action**:
  1. Echtes Line/Branch-Coverage-Reporting via `dotnet test` + `coverlet` + `ReportGenerator` einrichten
  2. CI-Gate definieren (z. B. min. 75% Application/Domain)
  3. GitHub Actions-Workflow mit Coverage-Upload
- **Nutzen**: Belastbare Coverage-Kennzahl statt Heuristik

---

## Epic L: Last- & Resilienztests

**Priority**: P3  
**Größe (geschätzt)**: M (8–13 Punkte)  
**Kategorie**: Testing, Performance, Resilience  
**Begründung**: Neu identifiziert; Circuit-Breaker-Schwellenwerte nicht unter Last validiert

### Sub-Tasks:

#### L.1: Lasttests für Checkout-Saga durchführen
**Story Points**: 5  
**Labels**: `type:test`, `priority:p3`, `epic:L`, `performance`  
**Beschreibung**:
- **Action**: Lasttest mit K6 oder Apache JMeter gegen lokale Aspire-Umgebung
- **Szenarien**: Normallast, Spitzenlast (10x), Fehlerrate-Szenarios (flaky Service)
- **Metriken**: P50/P95/P99 Latenz, Throughput, Circuit-Breaker-Auslösungen, Error-Rate
- **Ziel**: Circuit-Breaker-Schwellenwerte validieren (z. B. aktuell Threshold=5, Timeout=30s)

#### L.2: Chaos/Fault-Injection-Tests einrichten
**Story Points**: 5  
**Labels**: `type:test`, `priority:p3`, `epic:L`, `resilience`  
**Beschreibung**:
- **Tools**: Chaos Monkey, Gremlin, oder lokales Fault-Injection via Polly
- **Szenarien**: DB-Timeout, Redis-Fehler, Service-Bus-Ausfall, Netzwerk-Latenz
- **Ziel**: Observability bestätigen (Logs/Traces), Fallback-Pfade validieren

---

## Epic M: Security-Vertiefung Runde 2

**Priority**: P3  
**Größe (geschätzt)**: M (8–13 Punkte)  
**Kategorie**: Security  
**Begründung**: Zusätzliche Security-Härtung nach der initialen Bewertung

### Sub-Tasks:

#### M.1: BFF-Session-Dauer prüfen & justieren
**Story Points**: 2  
**Labels**: `type:fix`, `priority:p3`, `epic:M`, `security`  
**Beschreibung**:
- **Fund**: BFF `ExpireTimeSpan` auf 8 Stunden → kann zu lang sein je nach Risikoprofil
- **Action**:
  1. Sicherheits-Risikobewertung: Betriebsumgebung (SaaS/Enterprise), User-Aktivität
  2. ggf. auf 4–6h reduzieren oder rollenbasiert differenzieren
  3. Session-Timeout-Warning implementieren (z. B. JavaScript Alert 2 Min vor Expiry)

#### M.2: Partner-API-Key-Rotation dokumentieren & erzwingen
**Story Points**: 3  
**Labels**: `type:feat`, `priority:p3`, `epic:M`, `security`  
**Beschreibung**:
- **Fund**: Rotationsintervalle nicht dokumentiert oder erzwungen
- **Action**:
  1. Rotation-Policy in `docs/SECURITY-POLICY.md` festlegen (z. B. quarterly)
  2. Datenbankfeld `KeyRotatedAt` hinzufügen, EF-Migration
  3. Worker-Job, der > X Monate alte Keys flaggt/deaktiviert
  4. Admin-UI oder API für manuelle Rotation

#### M.3: Externer Penetrationstest durchführen
**Story Points**: 8  
**Labels**: `type:chore`, `priority:p3`, `epic:M`, `security`  
**Beschreibung**:
- **Action**: Red-Team oder externer Sicherheits-Consultant beauftragen
- **Umfang**: APIs, BFF, Authentifizierung, Session-Handling, Webhook-Verification
- **Ergebnis**: Findings in neues Backlog-Epic aufnehmen

---

## Epic N: Observability-Produktionstuning

**Priority**: P2  
**Größe (geschätzt)**: S (5–8 Punkte)  
**Kategorie**: Observability, FinOps  
**Begründung**: Trace-Sampling auf 100% → Kostenfactor in Produktion

### Sub-Tasks:

#### N.1: Trace-Sampling reduzieren (100% → ~10%)
**Story Points**: 3  
**Labels**: `type:fix`, `priority:p2`, `epic:N`, `observability`  
**Beschreibung**:
- **Fund**: OpenTelemetry `TracesSampleRatio=1.0` (100%) im aktuellen Setup
- **Problem**: Bei hohem Durchsatz enormer Daten-/Kostenfaktor in Azure Monitor
- **Action**:
  1. Baseline-Lasttest durchführen (siehe Epic L)
  2. Sampling auf 10–25% reduzieren (je nach Durchsatz-Charakteristik)
  3. `ParentBasedSampler` nutzen, um downstream propagiert zu samplen
  4. Configuration pro Environment (DEV: 100%, PROD: 10%)

#### N.2: Alerting & Kosten-Überwachung etablieren
**Story Points**: 2  
**Labels**: `type:chore`, `priority:p2`, `epic:N`, `finops`  
**Beschreibung**:
- **Action**:
  1. Azure Monitor-Alerts für spike in Ingestion Rate
  2. Budget-Alert wenn Moatsbudget > 80% erreicht
  3. Monatliches Cost-Review (App Insights-Kosten vs. Durchsatz)

---

## Epic O: Betriebs-Runbooks & Disaster Recovery

**Priority**: P3  
**Größe (geschätzt)**: M (8–13 Punkte)  
**Kategorie**: Operations, Resilience  
**Begründung**: Neue Idee; produktionsreif erfordert Betriebsprozesse

### Sub-Tasks:

#### O.1: Disaster-Recovery-Runbook schreiben
**Story Points**: 5  
**Labels**: `type:docs`, `priority:p3`, `epic:O`  
**Beschreibung**:
- **Content**:
  - Regional Failover (falls Multi-Region)
  - DB-Restore-Prozess (Point-in-Time Recovery)
  - Redis-Cluster-Ausfall
  - Outbox-Poison-Queue-Handling
  - Service-Bus-Neuinitialisierung
- **Format**: Step-by-step mit Screenshots, geschätzte RTO/RPO

#### O.2: Backup-Restore-Drill durchführen
**Story Points**: 3  
**Labels**: `type:chore`, `priority:p3`, `epic:O`  
**Beschreibung**:
- **Action**: Vierteljährlich ein Restore aus Backup auf Staging durchführen
- **Ziel**: Sicherstellen, dass Backups tatsächlich funktionieren (und nicht nur existieren)

#### O.3: Incident-Response-Prozess etablieren
**Story Points**: 3  
**Labels**: `type:docs`, `priority:p3`, `epic:O`  
**Beschreibung**:
- **Content**: On-Call-Rotation, Escalation-Path, Post-Mortem-Template, Betriebsstatus-Seite
- **Integration**: PagerDuty/OpsGenie, Slack-Kanal

---

## Epic P: Dependency- & Patch-Automatisierung

**Priority**: P3  
**Größe (geschätzt)**: S (5–8 Punkte)  
**Kategorie**: DevOps, Security  
**Begründung**: Komplement zu bestehenden Trivy/CodeQL-Workflows

### Sub-Tasks:

#### P.1: Dependabot oder Renovate konfigurieren
**Story Points**: 3  
**Labels**: `type:chore`, `priority:p3`, `epic:P`, `ci/cd`  
**Beschreibung**:
- **Action**: Dependabot (GitHub-native) oder Renovate aktivieren
- **Config**: Automatische Minor/Patch-Updates (mit auto-merge für grüne CI), Manual Review für Major
- **Nutzen**: Zero-Day-Patches schneller, weniger manuelle npm/NuGet-Checks

#### P.2: Security-Patch-Policy dokumentieren
**Story Points**: 2  
**Labels**: `type:docs`, `priority:p3`, `epic:P`  
**Beschreibung**:
- **Content**: SLA für kritische Patches (z. B. 48h für kritisch, 1 Woche für hoch)
- **Integration**: In `SECURITY-POLICY.md` verlinken

---

## Epic Q: API-Lifecycle-Policy

**Priority**: P3  
**Größe (geschätzt)**: S (5–8 Punkte)  
**Kategorie**: API Design  
**Begründung**: Neue Idee; Deprecation & Sunset-Policy für `/api/v1` → `/api/v2`

### Sub-Tasks:

#### Q.1: API-Deprecation-Policy schreiben
**Story Points**: 3  
**Labels**: `type:docs`, `priority:p3`, `epic:Q`, `api`  
**Beschreibung**:
- **Content**:
  - Mindestens 6–12 Monate Deprecation-Phase mit Warnings
  - Deprecation-Header (`Deprecation: true`, `Sunset: 2026-12-31`)
  - Client-Migrationsleitfaden
  - Beispiel: `/api/v1` Sunset auf 2027-01-01
- **Standard**: RFC 7231 / RFC 8594 Deprecation-Header

#### Q.2: Breaking-Change-Prozess etablieren
**Story Points**: 2  
**Labels**: `type:docs`, `priority:p3`, `epic:Q`, `api`  
**Beschreibung**:
- **Content**:
  - Breaking Changes erfordern Major Version `/api/v2`
  - Client-Notification (Email, Docs-Update, API-Alert)
  - Changelog-Entry verpflichtend

---

## Epic R: FinOps / Kostentransparenz

**Priority**: P3  
**Größe (geschätzt)**: S (5–8 Punkte)  
**Kategorie**: DevOps, Operations  
**Begründung**: Neue Idee; Azure Cost-Management

### Sub-Tasks:

#### R.1: Ressourcen-Tagging pro Service einführen
**Story Points**: 3  
**Labels**: `type:chore`, `priority:p3`, `epic:R`, `finops`  
**Beschreibung**:
- **Action**: Alle Azure-Ressourcen mit Tags versehen:
  - `service: catalog`, `service: ordering`, etc.
  - `environment: dev`, `prod`
  - `cost-center: engineering`
- **Nutzen**: Kostenaufteilung pro Service sichtbar

#### R.2: Cost Alerts & Budget-Limits setzen
**Story Points**: 2  
**Labels**: `type:chore`, `priority:p3`, `epic:R`, `finops`  
**Beschreibung**:
- **Action**:
  1. Azure Cost Management: Budget auf $X/Monat setzen
  2. Alerts bei 50%, 80%, 100%
  3. Anomalie-Detection aktivieren
- **Nutzen**: Unerwartete Kostenspitzen frühzeitig erkennen

#### R.3: Monatliches Cost-Review-Prozess
**Story Points**: 1  
**Labels**: `type:chore`, `priority:p3`, `epic:R`, `finops`  
**Beschreibung**:
- **Action**: Wiederkehrender Kalender-Termin, Ursachen-Analyse für Overspend
- **Ziel**: Costs im Budget halten

---

## Importieren ins GitHub Project

### Option A: Manuell (Schnell für Prototyping)

1. GitHub Project öffnen
2. Für jedes Item oben:
   - Click "+ Add Item"
   - Titel eingeben (z. B. "G.1: `contracts/CONVENTIONS.md` — gRPC-Section korrigieren")
   - Beschreibung + Links einfügen (aus dieser Doku)
   - Status: `Backlog`
   - Size: Fibonacci-Wert setzen
   - Priority: `🔴 P1`, `🟠 P2`, `🔵 P3`
   - Labels: Epic Tag (`epic:G` etc.) + Type Tag (`type:docs`, `type:feat` etc.)
   - Iteration: *leer lassen (für später)*

### Option B: Automatisiert (über GitHub API/CLI)

```bash
# Mit gh CLI (GitHub CLI):
gh project item-add <PROJECT_ID> --title "G.1: ..." \
  --body "Beschreibung..." \
  --field "Size:2" --field "Priority:P1" --field "Type:docs"
```

> Detailliert: [GitHub CLI Docs](https://cli.github.com/manual/gh_project)

---

## Story Points Schätzung — Fibonacci-Skala

Verwendet folgende Fibonacci-Werte:

| Size | Aufwand | Beispiel |
|------|---------|----------|
| **1** | 30 min | Ein-Zeiler-Fix, triviale Doc-Korrektur |
| **2** | 1 h | Einfache Doku, kleine Bug-Fix |
| **3** | 2 h | Medium-Doku, einfacher Refactor |
| **5** | 1 Tag | Neuer Validator, kleine Feature |
| **8** | 1,5–2 Tage | Feature + Tests, größerer Refactor |
| **13** | 3 Tage | Epic-Zeug, mehrere Komponenten |
| **21** | 1 Woche | Very Large, Multi-Service-Change |

---

## Nächste Schritte

1. ✅ **Diese Doku lesen & mit Moritz besprechen** → Status: Done
2. 🔜 **Alle Items ins GitHub Project eintragen** (Backlog)
3. 🔜 **Story Points pro Item schätzen** (mit User-Input)
4. 🔜 **First Sprint planen** (z. B. 5–6 P1-Items)
5. 🔜 **Sprint starten**

---

## Fragen an dich (Moritz)

Bevor du die Items eintragst, paar Clarifying Questions:

1. **Epics G–R zusammen nutzen, oder nur G–K priorisieren?**
   - G–K: Core-Produktionsreife (10–15 Story Points)
   - L–R: Zusätzliche Reife (FinOps, DR, Load-Tests)

2. **Sollen alle Items als separate GitHub Issues existieren, oder nur High-Level Epics als Issues + Sub-Tasks im Project?**
   - I würde empfehlen: Pro Epic ein Issue (G–R als 12 Issues), dann in Project die Sub-Tasks als Draft-Items

3. **Story Points klingen realistisch für dich, oder soll ich anpassen?**
   - Die Schätzungen basieren auf den Fund-Beschreibungen + Standard Development Overhead

4. **Priorisierst du P2 vs. P3, oder alle gleichzeitig ins Backlog?**
   - Vorschlag: Alle ins Backlog, aber First Sprint nur P1 + ein paar P2

Sag Bescheid, dann bauen wir das Board auf! 🚀

