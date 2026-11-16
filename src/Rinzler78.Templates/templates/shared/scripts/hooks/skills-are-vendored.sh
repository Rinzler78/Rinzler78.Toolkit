#!/usr/bin/env bash
# The vendored skills must be exactly those the manifest names, present and identical
# in both directories, and plain files: without Developer Mode a Windows checkout turns
# a symbolic link into a text file holding its target.
# rinzler-stage: pre-commit
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly MANIFEST="$REPO_ROOT/scripts/skills.manifest"
readonly A="$REPO_ROOT/.agents/skills"
readonly B="$REPO_ROOT/.claude/skills"

[[ -f "$MANIFEST" ]] || fail 'scripts/skills.manifest is missing'

expected=$(grep -vE '^[[:space:]]*(#|$)' "$MANIFEST" | awk '{ print $1 }' | sort)
[[ -n "$expected" ]] || fail 'scripts/skills.manifest names no skill'

links=$(find "$A" "$B" -type l 2>/dev/null || true)
[[ -z "$links" ]] || fail "symbolic link(s) among the skills: $links"

problems=0
for root in "$A" "$B"; do
  present=$(find "$root" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null | sort)
  while read -r name; do
    [[ -n "$name" ]] || continue
    [[ -f "$root/$name/SKILL.md" ]] || {
      printf '%s: skill %s is missing\n' "${root#"$REPO_ROOT"/}" "$name" >&2
      problems=$((problems + 1))
    }
  done <<<"$expected"
  while read -r name; do
    [[ -n "$name" ]] || continue
    grep -qx "$name" <<<"$expected" || {
      printf '%s: %s is not in scripts/skills.manifest\n' "${root#"$REPO_ROOT"/}" "$name" >&2
      problems=$((problems + 1))
    }
  done <<<"$present"
done

# The notice is the record of what is vendored: a pin edited without a refresh would
# otherwise pass while the skills still carry the old commit.
while read -r name _ ref _; do
  [[ -n "$name" ]] || continue
  for root in "$A" "$B"; do
    grep -qF -e "- \`$name\` at \`$ref\`" "$root/NOTICE.md" 2>/dev/null || {
      printf '%s/NOTICE.md does not record %s at %s\n' "${root#"$REPO_ROOT"/}" "$name" "$ref" >&2
      problems=$((problems + 1))
    }
  done
done < <(grep -vE '^[[:space:]]*(#|$)' "$MANIFEST")

((problems == 0)) || fail "$problems problem(s): run scripts/sync-skills.sh"

diff -r "$A" "$B" >/dev/null || fail '.agents/skills and .claude/skills diverge: run scripts/sync-skills.sh'
log "$(wc -l <<<"$expected" | tr -d ' ') skills vendored, identical in both directories"
