---
name: doc-sync
description: Analyzes the current branch diff (or a GitHub PR) and updates stale documentation. Maps changed source files to affected docs, reads them, proposes or applies targeted edits. Uses Context7 when a .csproj change introduces or modifies an external library.
triggers:
  - /doc-sync
---

# doc-sync

Scans the branch diff and updates stale documentation. Invoke before merging a PR.

Usage:
- `/doc-sync` — current branch vs. `master`
- `/doc-sync <PR-number>` — GitHub PR diff via `gh pr diff`

---

## Phase 1 — Collect the diff

If no PR number is given:
```
git diff master..HEAD --name-only
git diff master..HEAD
```

If a PR number is given:
```
gh pr diff <number>
gh pr diff <number> --name-only
```

Extract two lists:
- **source_files**: changed files under `src/`
- **csproj_changes**: any `.csproj` files with added/removed `<PackageReference>` lines

---

## Phase 2 — Map source files to documentation

Apply all rules that match. A single changed file may trigger multiple docs.

| Changed path pattern | Docs to inspect |
|---|---|
| `src/Services/<X>/` | `docs/architecture.md` (Feature Inventory table for service X), `src/Services/<X>/CLAUDE.md` |
| `src/BuildingBlocks/` | `docs/architecture.md` (BuildingBlocks section), root `CLAUDE.md` (Conventions section) |
| `src/**/*.razor`, `src/**/Web/`, `src/**/Bff/` | `docs/ui-conventions.md` |
| `src/**/Auth*`, `src/**/Identity*`, `src/**/Policy*` | `docs/auth/role-model.md` |
| New public interface, abstract pattern, or cross-cutting concern | `docs/adr/` — check if a new ADR is warranted; `docs/glossary.md` — check for new domain terms |
| `CHANGELOG.md` not yet updated | Add an entry under `[Unreleased]` for user-visible changes |
| `.csproj` with `<PackageReference>` additions | → Phase 4 (Context7) |

If no mapping matches, state that explicitly and stop — do not invent doc changes.

---

## Phase 3 — Read and diff

For each doc identified in Phase 2:
1. Read the full file.
2. Find the section(s) relevant to the changed code.
3. Determine whether the current text is stale or incomplete given the diff.
4. Draft a minimal, targeted update — no reformatting, no scope creep.

If a service-level `CLAUDE.md` does not exist yet and the service introduces a pattern exception, create one following the structure of `src/Services/Advisory/CLAUDE.md`.

---

## Phase 4 — Context7 (only when .csproj changed)

Execute this phase only if `csproj_changes` is non-empty.

For each added or updated `<PackageReference>`:
1. Call `mcp__plugin_azure_context7__resolve-library-id` with the package name.
2. Call `mcp__plugin_azure_context7__query-docs` with the resolved library ID and a topic derived from the diff (e.g. "pipeline behavior registration", "global query filter").
3. Use the returned API reference to ensure the documentation uses current, accurate syntax.

Do **not** call Context7 for packages with no doc change (version bump only with no usage change).

---

## Phase 5 — Present and apply

Present all proposed changes as a structured list:

```
File: docs/architecture.md
Section: Feature Inventory — Catalog Service
Change: Added "BrandManagement" feature row (CreateBrand, UpdateBrand, DeleteBrand)

File: src/Services/Catalog/CLAUDE.md  [new file]
Change: Documents brand soft-delete exception (brands use IsActive flag instead of IsDeleted)
```

Ask the user: apply all, apply selected, or discard.

On approval, apply with the Edit tool. Do **not** commit — leave staging and commit to the user.

---

## Constraints

- Do not update docs that are not directly affected by the diff.
- Do not reformat or rewrite sections unrelated to the change.
- Do not commit. Do not run `git add` or `git commit`.
- If a change is ambiguous (e.g. unclear whether it warrants a new ADR), flag it and ask rather than guessing.
- Context7 is for external library API accuracy only — not for internal architecture decisions.
