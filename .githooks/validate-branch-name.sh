#!/bin/sh
# Shared branch-name validation, called from pre-commit and pre-push.
# Schema: (feature|bug|refactor)/{issue-number}-{slug}
# {slug} must match the title of the referenced GitHub sub-issue (best-effort,
# only checked when `gh` is available and authenticated).

BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Direct work on these branches is exempt (merges, hotfix admin work).
case "$BRANCH" in
  master|main|HEAD)
    exit 0
    ;;
esac

PATTERN='^(feature|bug|refactor)/([0-9]+)-([a-z0-9]+(-[a-z0-9]+)*)$'

if ! echo "$BRANCH" | grep -qE "$PATTERN"; then
  echo ""
  echo "Branch name '$BRANCH' verstößt gegen die Namenskonvention."
  echo ""
  echo "Erlaubtes Schema:"
  echo "  feature/{issue-nummer}-{name}   z.B. feature/187-validatoren-fuer-payment-commands"
  echo "  bug/{issue-nummer}-{name}       z.B. bug/205-null-reference-in-checkout"
  echo "  refactor/{issue-nummer}-{name}  z.B. refactor/190-asnotracking-in-read-handlern"
  echo ""
  echo "{name} muss aus dem Titel des GitHub Sub-Issues abgeleitet sein (kebab-case, klein geschrieben)."
  echo ""
  exit 1
fi

ISSUE_NUMBER=$(echo "$BRANCH" | sed -E "s#$PATTERN#\2#")
SLUG=$(echo "$BRANCH" | sed -E "s#$PATTERN#\3#")

# Best-effort: compare slug against the actual issue title via gh CLI.
# Skipped silently if gh is missing, unauthenticated, or offline - this check
# never blocks a commit/push, it only warns.
if command -v gh > /dev/null 2>&1; then
  ISSUE_TITLE=$(gh issue view "$ISSUE_NUMBER" --json title -q .title 2>/dev/null)
  if [ -n "$ISSUE_TITLE" ]; then
    EXPECTED_SLUG=$(echo "$ISSUE_TITLE" | tr '[:upper:]' '[:lower:]' | \
      sed -E 's/ä/ae/g; s/ö/oe/g; s/ü/ue/g; s/ß/ss/g' | \
      sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//')
    if [ "$SLUG" != "$EXPECTED_SLUG" ]; then
      echo ""
      echo "Warnung: Branch-Name passt evtl. nicht zum Titel von Issue #$ISSUE_NUMBER."
      echo "  Issue-Titel:    $ISSUE_TITLE"
      echo "  Erwarteter Slug: $EXPECTED_SLUG"
      echo "  Aktueller Slug:  $SLUG"
      echo "(Dies blockiert den Commit nicht, bitte manuell prüfen.)"
      echo ""
    fi
  fi
fi

exit 0
