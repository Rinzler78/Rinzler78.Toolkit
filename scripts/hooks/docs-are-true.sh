#!/usr/bin/env bash
# Documentation is checked, not trusted. Existence proves nothing: a document that
# describes a command which no longer exists is worse than an absent one, because a
# reader acts on it.
#
# The verb inventory lives in three places — scripts/, the README, and the shared
# block of the two agent entry points — and three copies of one list drift. The
# entry points already agree with *each other*; nothing made them agree with the
# code, and a review caught exactly that. So every document that enumerates verbs is
# checked against the directory, in both directions.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly README="$REPO_ROOT/README.md"
[[ -f "$README" ]] || fail 'this repository has no README.md'

missing=0
require_section() {
  grep -qiE "^#{1,3} .*$1" "$README" || {
    printf 'README is missing a section about: %s\n' "$1"
    missing=$((missing + 1))
  }
}

require_section 'getting started|usage|commands'
require_section 'licence|license'

# Every verb that exists must be documented, and every documented verb must exist —
# in each document that enumerates them.
for document in "$README" "$REPO_ROOT/CLAUDE.md" "$REPO_ROOT/AGENTS.md"; do
  [[ -f "$document" ]] || continue
  name=$(basename "$document")

  for script in "$REPO_ROOT"/scripts/*.sh; do
    verb=$(basename "$script" .sh)
    [[ "$verb" == _* ]] && continue
    grep -qF "$verb" "$document" || {
      printf '%s does not mention the script verb: %s\n' "$name" "$verb"
      missing=$((missing + 1))
    }
  done

  while read -r mentioned; do
    [[ -f "$REPO_ROOT/scripts/$mentioned.sh" ]] || {
      printf '%s names a script that does not exist: scripts/%s.sh\n' "$name" "$mentioned"
      missing=$((missing + 1))
    }
  done < <(grep -oE 'scripts/[a-z-]+\.sh' "$document" | sed 's|scripts/||; s|\.sh||' | sort -u)
done

((missing == 0)) || fail "$missing documentation problem(s)"
log 'the documents agree with the repository'
