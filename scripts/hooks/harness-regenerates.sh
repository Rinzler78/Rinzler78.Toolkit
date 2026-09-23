#!/usr/bin/env bash
# The toolkit's own verification: its harness must be reproducible, byte for byte,
# from the template it ships. Until the template exists this check has nothing to
# compare, and says so rather than reporting a success it did not earn.
#
# Two kinds of file come out of a template, and conflating them makes this check
# either impossible or dishonest:
#
#   harness — owned by the template for the repository's whole life. Compared byte
#             for byte. A repository that edits one of these has forked its own
#             toolchain silently, which is the drift this check exists to catch.
#   seed    — expanded once, then owned by the repository: the README, the entry
#             points, the solution, the central versions, the dictionary, the code.
#             Repository-specific by construction; a README that could not be edited
#             would describe nothing.
#
# The distinction is declared by the template, in .template.config/seeds.txt, so that
# this check reads it rather than restating it — one list, as everywhere else here.
# rinzler-stage: pre-merge
# Installing the template pack and regenerating a whole repository is the most
# expensive check here, and what it protects — that the harness this repository
# publishes still reproduces itself — is a question about integrating work, not about
# saving a file.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly TEMPLATE_ROOT="$REPO_ROOT/src/Rinzler78.Templates"
readonly TEMPLATE=rinzler-lib
readonly SEEDS="$TEMPLATE_ROOT/templates/$TEMPLATE/.template.config/seeds.txt"

if [[ -z "$(find "$TEMPLATE_ROOT" -name '.template.config' -type d 2>/dev/null)" ]]; then
  log 'no template yet: nothing to regenerate, nothing verified'
  exit 0
fi

[[ -f "$SEEDS" ]] || fail "the template declares no seeds: $SEEDS"

seeds=()
while IFS= read -r pattern; do
  [[ -z "$pattern" || "$pattern" == '#'* ]] && continue
  seeds+=("$pattern")
done <"$SEEDS"
((${#seeds[@]} > 0)) || fail 'the seed list is empty: every generated file would be compared'

is_seed() {
  local path=$1 pattern
  for pattern in "${seeds[@]}"; do
    # shellcheck disable=SC2053 # the right-hand side is a pattern on purpose
    [[ "$path" == $pattern ]] && return 0
  done
  return 1
}

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

run dotnet new install "$TEMPLATE_ROOT" --force
# The repository's name, not the directory's: a worktree is named after its branch,
# and the generated seeds would then carry the branch slug.
name=$(cd "$REPO_ROOT" && basename -s .git "$(git config --get remote.origin.url 2>/dev/null)" 2>/dev/null)
[[ -n "$name" ]] || name=$(basename "$REPO_ROOT")

run dotnet new "$TEMPLATE" --output "$scratch/generated" --name "$name"

divergent=0
compared=0
seeded=0
while IFS= read -r -d '' generated; do
  relative=${generated#"$scratch/generated/"}
  committed="$REPO_ROOT/$relative"

  if is_seed "$relative"; then
    seeded=$((seeded + 1))
    continue
  fi

  compared=$((compared + 1))
  if [[ ! -f "$committed" ]]; then
    printf 'missing from the repository: %s\n' "$relative"
    divergent=$((divergent + 1))
  elif ! cmp -s "$generated" "$committed"; then
    printf 'differs from the template: %s\n' "$relative"
    divergent=$((divergent + 1))
  fi
done < <(find "$scratch/generated" -type f -print0)

((compared > 0)) || fail 'every generated file was declared a seed: nothing was verified'
((divergent == 0)) || fail "$divergent harness file(s) diverge from the template"
log "$compared harness file(s) regenerate byte-identically, $seeded seed(s) left to the repository"
