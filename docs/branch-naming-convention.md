# Branch-Namenskonvention

> Gültig ab 2026-07-04. Empfohlenes Schema; seit 2026-09-06 **nicht mehr durch lokale
> Git-Hooks erzwungen** (`.githooks/` wurde entfernt). Weiterhin relevant für die
> GitHub-Projects-Automatisierung unten, die auf diesem Schema aufbaut.

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

## Durchsetzung

Keine. Es gab bis 2026-09-06 lokale Git-Hooks (`.githooks/pre-commit`, `.githooks/pre-push`,
`.githooks/validate-branch-name.sh`), automatisch aktiviert über ein MSBuild-Target
(`InstallGitHooks`) in `Directory.Build.props`. Beides wurde entfernt — ein abweichender
Branch-Name blockiert weder Commit noch Push mehr. Das Schema oben ist reine Konvention;
Einhaltung ist nötig, damit die Projects-Automatisierung unten greift, wird aber nicht
technisch geprüft.

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
