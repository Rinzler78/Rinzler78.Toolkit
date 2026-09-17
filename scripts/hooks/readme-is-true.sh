#!/usr/bin/env bash
# A README is checked, not trusted. Existence proves nothing: a document that
# describes a command which no longer exists is worse than an absent one, because
# a reader acts on it.
#
# Two properties are asserted. The required sections are present, so a reader finds
# what the repository is, how to run it, and under what licence. And every script
# verb the README names actually exists, while every verb that exists is named —
# the two directions catch the two ways a README rots.
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

# Every verb that exists must be documented, and every documented verb must exist.
for script in "$REPO_ROOT"/scripts/*.sh; do
  verb=$(basename "$script" .sh)
  [[ "$verb" == _* ]] && continue
  grep -qF "$verb" "$README" || {
    printf 'README does not mention the script verb: %s\n' "$verb"
    missing=$((missing + 1))
  }
done

while read -r mentioned; do
  [[ -f "$REPO_ROOT/scripts/$mentioned.sh" ]] || {
    printf 'README names a script that does not exist: scripts/%s.sh\n' "$mentioned"
    missing=$((missing + 1))
  }
done < <(grep -oE 'scripts/[a-z-]+\.sh' "$README" | sed 's|scripts/||; s|\.sh||' | sort -u)

((missing == 0)) || fail "$missing README problem(s)"
log 'README is present and agrees with the repository'
