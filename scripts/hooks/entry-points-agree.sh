#!/usr/bin/env bash
# CLAUDE.md and AGENTS.md must carry an identical shared block. Prose cannot enforce
# agreement; a diff can.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

extract() {
  awk '/<!-- shared:start -->/{f=1;next} /<!-- shared:end -->/{f=0} f' "$1"
}

a=$(extract "$REPO_ROOT/CLAUDE.md")
b=$(extract "$REPO_ROOT/AGENTS.md")

[[ -n "$a" ]] || fail 'CLAUDE.md carries no shared block'
[[ -n "$b" ]] || fail 'AGENTS.md carries no shared block'

if [[ "$a" != "$b" ]]; then
  diff <(printf '%s\n' "$a") <(printf '%s\n' "$b") || true
  fail 'CLAUDE.md and AGENTS.md have diverged'
fi
log 'entry points agree'
