# Start Task

Start work on a GitHub (sub-)issue: derive the correct branch name and check it out.

## Usage

```
/start-task <issue-number>
```

- `<issue-number>` — the GitHub issue number, e.g. `123` or `#123`. **Required** — refuse to proceed without it.

## What to do

1. **Parse the issue number** from the argument (strip a leading `#` if present). If no number was given or it isn't numeric, stop and ask for one — do not guess.

2. **Fetch the issue title and type** via `gh issue view <number> --json title,labels,url`. If `gh` fails (not installed, not authenticated, issue not found), stop and report the error — do not fabricate a branch name.

3. **Determine the branch prefix** from the issue's labels/title per [docs/branch-naming-convention.md](../../docs/branch-naming-convention.md):
   - `bug` → fix/defect labels or title starting with "Fix"/"Bug"
   - `refactor` → refactor labels or title starting with "Refactor"
   - `feature` → everything else (default)

   If it's ambiguous, ask the user to pick one of `feature`/`bug`/`refactor` rather than guessing silently.

4. **Build the slug** from the issue title: lowercase, transliterate `ä→ae`, `ö→oe`, `ü→ue`, `ß→ss`, then replace every run of non-`[a-z0-9]` characters with a single `-`, and trim leading/trailing `-`. This must match the slug logic in `.githooks/validate-branch-name.sh` exactly, since that hook validates it later.

5. **Assemble the branch name**: `{prefix}/{issue-number}-{slug}`.

6. **Check for a clean working tree.** If there are uncommitted changes, stop and ask the user how to proceed (stash, commit, or abort) instead of silently carrying them onto the new branch.

7. **Create and check out the branch** from the current `HEAD` (normally `master`, up to date):
   ```
   git checkout -b {branch-name}
   ```
   If a branch with that name already exists locally, check it out instead of erroring, and tell the user.

8. **Report** the branch name, the issue title/URL, and remind the user this branch push will move the linked GitHub Project card to "In progress" automatically (per the branch-created workflow).

## Constraints

- Never invent a branch name without having read the real issue title via `gh issue view`.
- Never force-checkout over uncommitted changes.
- Do not push the branch — this command only creates and checks it out locally.
