# Story Points Schätzungs-Guide für OrderSphere Epics

> Quick Reference für Story-Point-Vergabe (Fibonacci: 1, 2, 3, 5, 8, 13, 21)

**Status**: Alle Epics (#171–182) sind erstellt, warten auf Story-Point-Schätzung

---

## Schätzungs-Matrix

| Epic | Titel | Empf. Size | Begründung | Gewöhnlich |
|------|-------|-----------|-----------|-----------|
| **G** | Dokumentations-Korrektionen | **5** | 3 kleine Doku-Fixes, schnell machbar | 1–2h pro Item |
| **H** | Validator-Härtung | **8** | Payment (5) + Partners (3) + Compiler-Fix (5) = echte Arbeit | 2–3 Tage |
| **I** | EF-Core & Zeit-Abstraktion | **13** | 49 Handler durchgehen + TimeProvider impl. = groß | 3–4 Tage |
| **J** | Fehlerbehandlung | **2** | 4 Stellen, 1 Log-Zeile pro Stelle | 30 min |
| **K** | Testabdeckung | **13** | Payment Tests (8) + Partners/Invoice (5) + Coverage (3) | 3–4 Tage |
| **L** | Lasttests | **8** | K6-Setup + Chaos-Tests, aber einfache Szenarien | 2–3 Tage |
| **M** | Security-Vertiefung | **8** | Session-Check (2) + Key-Rotation (3) + Pentest (8) = aber Pentest extern | 2–3 Tage |
| **N** | Observability-Tuning | **5** | Sampling reduzieren + Alerts, schnelle Wins | 1–2 Tage |
| **O** | DR & Runbooks | **8** | Dokumentation + Drill-Plan, aber substanziell | 2–3 Tage |
| **P** | Dependency-Automation | **5** | Dependabot config + Policy-Doku | 1–2 Tage |
| **Q** | API-Lifecycle | **5** | Doku + Policy, kleinere Policy | 1–2 Tage |
| **R** | FinOps | **5** | Tagging + Alerts + Prozess, sehr machbar | 1–2 Tage |

---

## Summe pro Priority-Level

| Level | Epics | Total | First Sprint Kandidat? |
|-------|-------|-------|----------------------|
| **P1** | G, H | 13 | ✅ JA (+ ein paar P2) |
| **P2** | I, J, N | 18 | ✅ Ein oder zwei mitnehmen |
| **P3** | K, L, M, O, P, Q, R | 49 | ❌ Später, nach P1/P2 |

---

## Wie Story Points ins Project eintragen

### Via GitHub UI (für dich am einfachsten):

1. Öffne: https://github.com/moritzwaldau/OrderSphere/projects/1
2. Klick auf Issue (#171, etc.)
3. Im Sidebar: Feld **Size** → Fibonacci-Wert eingeben
4. Speichern (Auto)

### Schnell-Weg (alle auf einmal):

1. Project-Board öffnen (Table View)
2. Spalte **Size** sichtbar? (Falls nicht: + Fields hinzufügen)
3. Pro Zeile Size-Wert eingeben (5, 8, 13, etc.)

---

## First Sprint Planung (Freitag)

**Ziel**: ~20–30 Story Points

**Kandidaten (nach Priority)**:
```
P1:
  ✓ #171 (Epic G) = 5 Points
  ✓ #172 (Epic H) = 8 Points
  Subtotal P1: 13 Points

P2 (wähle ein paar):
  ✓ #174 (Epic J) = 2 Points [QUICK WIN]
  ✓ #178 (Epic N) = 5 Points [QUICK WIN]
  ~ #173 (Epic I) = 13 Points [zu groß für Sprint 1]

Vorschlag für Sprint 1:
  G (5) + H (8) + J (2) + N (5) = 20 Points ✅
```

---

## Checklist vor Sprint 1 Start

- [ ] Alle 12 Epics haben einen Size-Wert
- [ ] First Sprint Issues sind ausgewählt (Iteration gesetzt)
- [ ] Status auf `Todo` gesetzt für Sprint-Items
- [ ] Optional: Labels (priority:p1, epic:g, etc.) hinzugefügt
- [ ] First Sprint Starts: Freitag diese Woche

---

**Feedback?** Wenn die Schätzungen nicht passen: `docs/github-projects-backlog.md` konsultieren für Details pro Sub-Task.
