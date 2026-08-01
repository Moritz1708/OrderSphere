#!/usr/bin/env bash
# OrderSphere convention check — PostToolUse hook for Write|Edit.
#
# Reads the hook payload on stdin, inspects the edited file, and feeds any
# CLAUDE.md convention violation back to the model as additionalContext.
# Advisory only: never blocks, always exits 0.
#
# Checks are deliberately grep-based (no MSBuild) so the hook stays in the
# millisecond range. Each one was validated against the full repository at
# authoring time and produced zero false positives.

set -uo pipefail

payload=$(cat)
file=$(printf '%s' "$payload" | jq -r '.tool_response.filePath // .tool_input.file_path // empty')
[ -z "$file" ] && exit 0

# Normalise Windows paths (E:\a\b -> E:/a/b) so grep/test work under Git Bash.
file=${file//\\//}
[ -f "$file" ] || exit 0

repo=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
repo=${repo//\\//}

findings=()

case "$file" in
  *.cs)
    # 1. Sync-over-async. CLAUDE.md § Conventions: all I/O is async/await.
    #    .Result is deliberately NOT matched — Result<T> is the project's error
    #    type and would generate constant false positives.
    if hits=$(grep -nE '\.GetAwaiter\(\)\.GetResult\(\)|\.Wait\(\)' "$file"); then
      findings+=("Sync-over-async in $file:"$'\n'"$hits"$'\n'"Rule: CLAUDE.md § Conventions — all I/O is async/await; no .Wait()/.GetAwaiter().GetResult().")
    fi

    # 2. Application layer must not reference Infrastructure.
    case "$file" in
      *.Application/*|*/Application/*)
        if hits=$(grep -nE '^using .*\.Infrastructure' "$file"); then
          findings+=("Layering violation in $file:"$'\n'"$hits"$'\n'"Rule: CLAUDE.md § Architecture — Application never references Infrastructure.")
        fi
        ;;
    esac

    # 3. EF configuration for an AuditableEntity must carry the soft-delete filter.
    #    Only fires when the configured type genuinely inherits AuditableEntity,
    #    so Inbox/Outbox/saga/event-store tables are correctly ignored.
    case "$file" in
      */EntityConfigurations/*)
        if ! grep -q 'HasQueryFilter' "$file"; then
          entity=$(grep -oE 'IEntityTypeConfiguration<[A-Za-z0-9_]+>' "$file" | head -1 | sed 's/.*<//;s/>//')
          if [ -n "$entity" ] && grep -rqE "class[[:space:]]+${entity}\b[^{]*:[^{]*AuditableEntity" --include=*.cs "$repo/src" 2>/dev/null; then
            findings+=("Missing soft-delete filter in $file: $entity inherits AuditableEntity but its configuration has no HasQueryFilter."$'\n'"Rule: CLAUDE.md § Conventions — every AuditableEntity gets builder.HasQueryFilter(x => !x.IsDeleted).")
          fi
        fi
        ;;
    esac
    ;;

  *.razor)
    # 4. Colours must resolve through theme tokens so brand switching works.
    if hits=$(grep -nE '#[0-9a-fA-F]{6}\b' "$file"); then
      findings+=("Hardcoded hex colour in $file:"$'\n'"$hits"$'\n'"Rule: docs/ui-conventions.md § 1 — use var(--mud-palette-*) / var(--os-*) tokens; literal hex does not follow a brand switch.")
    fi
    ;;
esac

[ ${#findings[@]} -eq 0 ] && exit 0

printf '%s\n' "${findings[@]}" \
  | jq -Rs '{hookSpecificOutput:{hookEventName:"PostToolUse",additionalContext:("OrderSphere convention check:\n" + .)}}'
