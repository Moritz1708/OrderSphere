# GitHub Projects Governance Guide für OrderSphere

> Dieses Dokument beschreibt die Struktur, Workflows und Best Practices für Task-Management in GitHub Projects (OrderSphere).

**Gültig ab**: 2026-07-04  
**Letzte Änderung**: 2026-07-04  
**Team**: Solo (Moritz Waldau)

---

## 1. Überblick

GitHub Projects ist die zentrale Task- und Sprint-Management-Plattform für OrderSphere. Es dient als Single Source of Truth für:
- Feature-Planung und Backlog
- 2-Wochen-Sprint-Planung (Iteration)
- Release-Roadmap (3+ Monate Ausblick)
- Abhängigkeiten zwischen Issues und PRs
- Automatisierte Workflow-Übergänge

**Nicht auf GitHub Projects**: Architektur-Entscheidungen, Debugging-Notes, Code Reviews → diese gehören ins Repo (Markdown in `docs/`), nicht ins Project Board.

---

## 2. Project-Struktur

### 2.1 Views

Das OrderSphere Project hat vier Views:

#### **Backlog** (Table View)
- Alle offenen Issues/PRs, nicht in Sprint
- Spalten: **Title** | **Repository** | **Status** | **Size** | **Priority** | **Labels**
- Filter: `is:open -label:in-sprint`
- Nutzen: Backlog triaging vor Sprint-Planning
- Sortierung: Priority (desc) → Size (asc)

#### **Current Sprint** (Table View)
- Nur Items im aktuellen Iteration-Zyklus
- Spalten: **Title** | **Status** | **Size** | **Assignee** (always "Moritz") | **Progress**
- Filter: `iteration:@current`
- Nutzen: Fokus auf aktuelle 2-Wochen
- Sortierung: Status → Size

#### **Board** (Kanban View)
- Workflow-Visualisierung
- Spalten (Status-Field): **Backlog** → **Todo** → **In Progress** → **In Review** → **Done**
- Automatische Card-Bewegung bei Status-Änderung (siehe Automation)
- Nutzen: Tages-Tracking, Bottleneck-Erkennung

#### **Roadmap** (Roadmap View)
- 3-Monate-Ausblick nach Iteration/Zeit
- Items gruppiert nach zielter Iteration (Sprint)
- Nutzen: Langfristige Feature-Planung sichtbar machen
- Aktualisierung: Quartalsweise Review + dynamisch bei neuen Items

---

### 2.2 Custom Fields

| Field-Name | Typ | Nutzen | Werte/Skala |
|------------|-----|--------|-------------|
| **Size** | Number | Fibonacci Story Points | 1, 2, 3, 5, 8, 13, 21, 34 |
| **Priority** | Single Select | Priorisierung im Backlog | `🔴 Critical` \| `🟠 High` \| `🟡 Medium` \| `🔵 Low` |
| **Status** | Single Select | Workflow-Status | `Backlog` \| `Todo` \| `In Progress` \| `In Review` \| `Done` |
| **Iteration** | Iteration | Sprint-Zugehörigkeit | `Sprint 1 (2026-07-04–07-18)` \| `Sprint 2` \| ... |
| **Type** | Single Select | Item-Kategorie | `Feature` \| `Bug` \| `Refactor` \| `Chore` \| `Docs` |

---

## 3. Workflow-Prozess

### 3.1 Sprint-Zyklus (2 Wochen)

**Freitag vor Sprint Start (Planning)**
1. Ordne Top-10 Backlog-Items nach Priority
2. Schätze unkategorisierte Items mit Fibonacci (Size)
3. Wähle Items für Sprint: Ziel ~20–30 Points (basierend auf Velocity)
4. Setze Iteration auf `Sprint N (2026-xx-xx–2026-xx-xx)`
5. Setze Initial-Status aller Sprint-Items auf `Todo`

**Montag–Freitag (Sprint Execution)**
- Items von `Todo` → `In Progress` (sobald du anfängst)
- Custom Workflow: PR erstellt → automatisch `In Progress` (siehe Automation)
- Custom Workflow: PR merged/closed → automatisch `Done` (siehe Automation)

**Freitag nach Sprint Ende (Retrospektive)**
1. Alle `In Progress` / `In Review` Items → `Done` falls merged, sonst `Backlog`
2. Velocity berechnen: ∑(Points von Done-Items)
3. Dokumentiere Velocity im nächsten Sprint für bessere Schätzung
4. Neue Sprint-Iteration erstellen für nächste 2 Wochen

---

### 3.2 Item-Lifecycle

```
┌─────────────────────────────────────────────────────────────┐
│ Issue erstellt oder Manual hinzugefügt                      │
└──────────────────────────────────────────────────────────────┘
                           ↓
                    ┌──────────────────┐
                    │ Backlog (Default)│
                    └──────────────────┘
                           ↓
          ┌─────────────────────────────────────┐
          │ Sprint-Planning: In Iteration setzen│
          │ + Size schätzen + Priority setzen   │
          └─────────────────────────────────────┘
                           ↓
                    ┌──────────────────┐
                    │  Todo            │
                    └──────────────────┘
                           ↓
        ┌──────────────────────────────────┐
        │ Manuelle Änderung zu In Progress │
        │ ODER PR erstellt (auto via GH    │
        │ Actions) → Status auto zu In     │
        │ Progress                         │
        └──────────────────────────────────┘
                           ↓
                    ┌──────────────────┐
                    │ In Progress      │
                    └──────────────────┘
                           ↓
      ┌───────────────────────────────────────┐
      │ PR erstellt / Review startet?         │
      │ → Status manual zu In Review          │
      │ (oder auto via GH Actions wenn tagged)│
      └───────────────────────────────────────┘
                           ↓
                    ┌──────────────────┐
                    │ In Review        │
                    └──────────────────┘
                           ↓
      ┌───────────────────────────────────────┐
      │ PR merged oder Issue geschlossen      │
      │ → Status auto zu Done (via GH Actions)│
      └───────────────────────────────────────┘
                           ↓
                    ┌──────────────────┐
                    │ Done             │
                    └──────────────────┘
```

---

### 3.3 Status-Definitionen

| Status | Bedingung | Wer ändert |
|--------|-----------|-----------|
| **Backlog** | Nicht geplant oder nächste Sprint zugeordnet | Manuell (Sprint-Planning) |
| **Todo** | In aktuellem Sprint, noch nicht gestartet | GH Actions (Sprint-Start) oder Manuell |
| **In Progress** | Du arbeitest dran, PR erstellt | GH Actions (automatisch bei PR-Erstellung) |
| **In Review** | PR offen, wartet auf Review/Feedback | Manuell (du selbst oder GH Actions wenn Review startet) |
| **Done** | PR merged oder Issue geschlossen | GH Actions (automatisch bei merge/close) |

---

## 4. GitHub Actions Automation

### 4.1 Trigger-Szenarien

Folgende GitHub Actions automatisieren den Workflow:

#### **Szenario 1: Issue in Project aufnehmen (Backlog)**
- **Trigger**: Neues Issue mit Label `ordersphere` erstellt
- **Action**: Automatisch zum Project hinzufügen, Status `Backlog`, alles andere leer
- **Workflow**: `.github/workflows/add-to-project.yml`

#### **Szenario 2: PR erstellt → Status zu "In Progress"**
- **Trigger**: PR erstellt & mit Issue verlinkt (Closes #123)
- **Action**: 
  - Zugehöriges Issue finden
  - Status zu `In Progress` setzen
  - Progress-Field auf Linked PR aktualisieren
- **Workflow**: `.github/workflows/pr-opened.yml`

#### **Szenario 3: PR merged → Status zu "Done"**
- **Trigger**: PR merged
- **Action**:
  - Zugehöriges Issue Status zu `Done` setzen
  - Timestamp in "Completed" speichern
- **Workflow**: `.github/workflows/pr-merged.yml`

#### **Szenario 4: Issue geschlossen (ohne PR) → Status zu "Done"**
- **Trigger**: Issue geschlossen
- **Action**: Status zu `Done` setzen
- **Workflow**: `.github/workflows/issue-closed.yml`

---

### 4.2 Workflows bereitstellen

**Ort**: `.github/workflows/` im Repo

**Voraussetzung**: GitHub Project GraphQL-ID (findest du in Project Settings)

Siehe Beispiel-Workflows in [Kapitel 5](#5-technische-implementierung).

---

## 5. Technische Implementierung

### 5.1 GitHub Project GraphQL IDs finden

1. Gehe zu dein OrderSphere GitHub Project
2. Öffne DevTools (F12) → Network Tab
3. Lade die Project-Seite neu
4. Filtere nach GraphQL-Requests
5. Suche die Antwort mit `projectV2` → kopiere die `id`

Beispiel:
```
{
  "projectV2": {
    "id": "PVT_kwDOF1234567890abcdef",
    ...
  }
}
```

---

### 5.2 Workflows

#### **`.github/workflows/add-to-project.yml`**
```yaml
name: Add issue to project
on:
  issues:
    types: [opened, reopened]

jobs:
  add-to-project:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/add-to-project@v0.4.0
        with:
          project-url: https://github.com/users/moritzwaldau/projects/1
          github-token: ${{ secrets.ADD_TO_PROJECT_TOKEN }}
          labeled: 'ordersphere'
```

**Token**: Erstelle einen Personal Access Token mit `project` Scope
- Gehe zu GitHub Settings → Developer settings → Personal access tokens
- Generiere neuen Token mit Scope: `repo`, `project`, `write:org`
- Speichern als Repository Secret `ADD_TO_PROJECT_TOKEN`

---

#### **`.github/workflows/pr-to-in-progress.yml`**
```yaml
name: PR opened - set to In Progress
on:
  pull_request:
    types: [opened, ready_for_review]

jobs:
  update-project:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/github-script@v7
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          script: |
            const query = `
              query {
                repository(owner: "${{ github.repository_owner }}", name: "${{ github.event.repository.name }}") {
                  pullRequest(number: ${{ github.event.pull_request.number }}) {
                    closingIssuesReferences(first: 1) {
                      nodes {
                        projectItems(first: 1) {
                          nodes {
                            id
                            project {
                              id
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            `;
            
            const result = await github.graphql(query);
            const projectItem = result.repository.pullRequest.closingIssuesReferences.nodes[0]?.projectItems.nodes[0];
            
            if (projectItem) {
              const updateMutation = `
                mutation {
                  updateProjectV2ItemFieldValue(input: {
                    projectId: "${projectItem.project.id}"
                    itemId: "${projectItem.id}"
                    fieldId: "PVTF_kwDP..._Status"
                    value: {singleSelectOptionId: "PVTSO_...In Progress"}
                  }) {
                    projectV2Item {
                      id
                    }
                  }
                }
              `;
              
              await github.graphql(updateMutation);
              console.log('Status set to In Progress');
            }
```

**Hinweis**: Field-IDs müssen manuell aus Project Settings extrahiert werden (GraphQL Introspection oder UI-Inspection).

---

#### **`.github/workflows/pr-merged.yml`**
```yaml
name: PR merged - set to Done
on:
  pull_request:
    types: [closed]

jobs:
  update-project:
    if: github.event.pull_request.merged == true
    runs-on: ubuntu-latest
    steps:
      - uses: actions/github-script@v7
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          script: |
            // Ähnlich wie pr-to-in-progress.yml, aber Status auf "Done" setzen
            // (siehe obiges Beispiel als Template)
            console.log('PR merged - setting to Done');
```

---

### 5.3 Vereinfachte Alternative: Manuelle Automation

Falls die GitHub Actions-Komplexität zu hoch ist, kannst du auch:
- **Status manuell** updaten (schnell via Project UI)
- **Built-in GitHub Automation** nutzen:
  - Settings → Automations → "Auto-add closed issues to Done"
  - Settings → Automations → "Auto-archive items older than 30 days"

Dies reduziert Konfiguration, braucht aber mehr manuelle Updates.

---

## 6. Best Practices

### 6.1 Issue-Erstellung
- **Titel**: Prägnant, Action-orientiert (`Add authentication to API` statt `Auth`)
- **Description**: User Story Format: `As [role], I want [feature], so that [benefit]`
- **Labels**: Mindestens `ordersphere` + `Type: Feature/Bug/...`
- **Size**: Nach Schätzung via Fibonacci setzen (oder leer für Backlog Triage)
- **Priority**: `High` für aktuelle Sprint, `Medium/Low` für Backlog

### 6.2 PR-Workflow
- **PR-Template**: Link zum Issue in Description: `Closes #123`
- **Auto-Link**: GitHub parsed `Closes #123` automatisch
- **Status**: Sollte auto auf `In Progress` gehen (via Workflow)
- **Review**: PR Status auf `In Review` ändern vor Code Review (manuell oder auto)
- **Merge**: Nach Approve → Status auto auf `Done`

### 6.3 Sprint-Planning (Freitag)
1. Velocity aus letztem Sprint berechnen
2. Top-Backlog-Items mit Priority checken
3. Neue Items schätzen (Fibonacci)
4. Items in neue Iteration ziehen bis ~Velocity erreicht
5. Sprint-Zyklus dokumentieren (Sprint 1 Start/End Datum)

### 6.4 Täglich
- **Morgens**: Current Sprint View öffnen → Items checken
- **Bei Start**: Issue-Status `Todo` → `In Progress`
- **Bei PR-Erstellung**: PR mit `Closes #123` linken (Automation macht Rest)
- **Nach Code Review**: Status `In Review` (manuell)
- **Nach Merge**: Status sollte auto `Done` sein

---

## 7. Glossar

| Begriff | Definition |
|---------|-----------|
| **Sprint** | 2-Wochen-Iteration; Planung Freitag, Ausführung Mo–Fr, Abschluss Freitag |
| **Size (Story Points)** | Fibonacci-Schätzung (1,2,3,5,8,13,21,34); 1pt ≈ 30min, 5pt ≈ 1–2 Tage |
| **Velocity** | ∑ Punkte der in Sprint fertiggestellten Items (für nächste Sprint-Planung) |
| **Priority** | `🔴 Critical` (sofort) \| `🟠 High` (diesen Sprint) \| `🟡 Medium` (bald) \| `🔵 Low` (irgendwann) |
| **Status** | `Backlog` → `Todo` → `In Progress` → `In Review` → `Done` |
| **Closing Issue** | Issue, das per PR (via `Closes #123`) automatisch geschlossen wird |

---

## 8. Troubleshooting

### Problem: Workflow triggert nicht
- **Check**: PAT Token expiriert? → Neu generieren in GitHub Settings
- **Check**: Project-ID korrekt? → GraphQL Query testen (GitHub Explorer)
- **Check**: Field-ID korrekt? → Workflow mit `console.log` debuggen

### Problem: Status ändert sich nicht
- **Check**: Ist Issue wirklich in Project? → Manuell in UI hinzufügen
- **Check**: Ist PR mit `Closes #123` verlinkt? → Manuell in PR-Description ergänzen

### Problem: zu viele Automationen/Overhead
- **Option 1**: Nur Status-Updates automatisieren, Rest manuell
- **Option 2**: GitHub Actions komplett ignorieren, alles manuell (schnell am Anfang)
- **Option 3**: Schrittweise einführen (erst Add-to-Project, später PR-Workflows)

---

## 9. Änderungshistorie

| Datum | Änderung | Grund |
|-------|----------|-------|
| 2026-07-04 | Initial | OrderSphere GitHub Projects Setup |

---

## 10. Referenzen

- [GitHub Projects Documentation](https://docs.github.com/en/issues/planning-and-tracking-with-projects)
- [GitHub Actions for Projects](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project)
- [Best Practices for Projects](https://docs.github.com/en/issues/planning-and-tracking-with-projects/learning-about-projects/best-practices-for-projects)

---

**Fragen?** Diese Dokumentation ist lebendig. Bei Fragen oder Anpassungsbedarf: `docs/github-projects-governance.md` updaten + commiten.
