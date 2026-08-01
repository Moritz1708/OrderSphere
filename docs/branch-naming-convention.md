# Branch-Namenskonvention

> Gültig ab 2026-07-04. Gilt für alle Arbeit in diesem Repository.

## Schema

```
feature/{issue-nummer}-{name}
bug/{issue-nummer}-{name}
refactor/{issue-nummer}-{name}
chore/{name}
```

- **`feature/`** — neue Funktionalität
- **`bug/`** — Fehlerbehebung
- **`refactor/`** — Umstrukturierung ohne Verhaltensänderung
- **`chore/`** — Arbeit ohne verknüpftes Issue (Tooling, Doku, CI, AI-Setup); **ohne** Issue-Nummer
- **`{issue-nummer}`** — Nummer des GitHub Sub-Issues, an dem gearbeitet wird
- **`{name}`** — kebab-case-Slug des Sub-Issue-Titels (Kleinschreibung, Umlaute transliteriert: ä→ae, ö→oe, ü→ue, ß→ss)

**Beispiel**: Sub-Issue #187 "Validatoren für Payment-Commands ergänzen"
→ Branch: `feature/187-validatoren-fuer-payment-commands-ergaenzen`

Andere Branch-Namen (abweichende Präfixe oder fehlende Issue-Nummer bei `feature`/`bug`/`refactor`) sind nicht zulässig. `master`/`main` sind von der Prüfung ausgenommen.

### `chore/` — Arbeit ohne Issue

Nicht jede Änderung hat ein Sub-Issue: Tooling, Doku, CI-Anpassungen, AI-Setup. Dafür ist `chore/{name}` vorgesehen — gleiche Slug-Regeln (kebab-case, Kleinschreibung), aber keine Issue-Nummer.

`chore/`-Branches lösen **bewusst keine** Projects-Automatisierung aus: `project-branch-created.yml` reagiert nur auf `feature`/`bug`/`refactor`, es wird also keine Board-Karte erwartet oder verschoben.

### Notausgang

Für alles, was in kein Schema passt, lässt sich die Prüfung explizit übergehen:

```bash
ORDERSPHERE_SKIP_BRANCH_CHECK=1 git commit -m "..."
```

Für eine ganze Shell-Sitzung: `export ORDERSPHERE_SKIP_BRANCH_CHECK=1`. Die Hooks melden dann sichtbar, dass die Prüfung übersprungen wurde — der Bypass ist bewusst laut, nicht still.

## Durchsetzung

Wird lokal über Git-Hooks erzwungen, nicht über GitHub Actions:

- **`.githooks/pre-commit`** — blockiert jeden Commit auf einem nicht-konformen Branch
- **`.githooks/pre-push`** — blockiert zusätzlich das Pushen (verhindert dadurch faktisch auch die PR-Erstellung, da ein nicht gepushter Branch auf GitHub nicht existiert)
- **`.githooks/validate-branch-name.sh`** — gemeinsame Prüflogik beider Hooks

Beide Hooks vergleichen zusätzlich den Slug gegen den echten Issue-Titel (per `gh issue view`, sofern `gh` lokal installiert und authentifiziert ist). Diese Prüfung ist nur eine **Warnung**, kein Blocker — sie funktioniert nicht offline und nicht ohne `gh`-Login.

### Automatische Aktivierung

Die Hooks werden **nicht** durch bloßes Einchecken aktiv — Git liest `core.hooksPath` aus der lokalen, nicht versionierten Git-Konfiguration. Damit die Hooks bei jedem Clone garantiert greifen, setzt ein MSBuild-Target in [`Directory.Build.props`](../Directory.Build.props) (`InstallGitHooks`) bei jedem lokalen `dotnet build`/`dotnet restore` automatisch:

```
git config core.hooksPath .githooks
```

Das Target überspringt sich selbst in CI-Umgebungen (`$(CI) != ''`), dort sind die Hooks irrelevant.

## GitHub-Projects-Automatisierung

Zusätzlich zur lokalen Namensprüfung übernimmt die Branch-Nummer eine zweite Aufgabe: Sie ermöglicht es GitHub-Actions-Workflows, das zugehörige Sub-Issue im [OrderSphere-Project](https://github.com/users/moritzwaldau/projects/1) automatisch weiterzuschalten — ganz ohne `Closes #123`-Text im PR.

| Trigger | Workflow | Neuer Status |
|---|---|---|
| Branch `feature/187-...` wird erstellt | `.github/workflows/project-branch-created.yml` | `In progress` |
| PR von diesem Branch gegen `master` geöffnet | `.github/workflows/project-pr-opened.yml` | `In review` |
| PR gemerged nach `master` | `.github/workflows/project-pr-merged.yml` | `Done` (schließt zusätzlich das Issue) |

Diese Workflows benötigen ein Repository-Secret `PROJECT_AUTOMATION_TOKEN` — ein Personal Access Token mit den Scopes `repo` und `project` (siehe Setup-Anleitung unten). Ohne dieses Secret laufen die Workflows durch, ohne den Projekt-Status zu ändern (kein harter Fehler, aber auch keine Wirkung).

### Secret einrichten (einmalig, durch dich)

1. GitHub → Settings → Developer settings → **Personal access tokens** → **Fine-grained tokens** (oder Classic, falls Fine-grained für Projects noch eingeschränkt ist)
2. Scope/Permissions: **`repo`** (voller Zugriff für dieses Repo) + **`project`** (Read/Write)
3. Token generieren, Wert kopieren
4. Im Repo: `gh secret set PROJECT_AUTOMATION_TOKEN --repo moritzwaldau/OrderSphere` → Token einfügen wenn danach gefragt (oder über GitHub UI: Settings → Secrets and variables → Actions → New repository secret)

Empfehlung: Diesen Token **nicht** mit dem eigenen `gh`-CLI-Login-Token identisch verwenden — ein eigens dafür erstellter, eng gescopter Token begrenzt den Schaden, falls er einmal in Workflow-Logs auftaucht.
