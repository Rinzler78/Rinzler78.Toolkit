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
# Every published template, not only the first: the harness is shared, but each
# template wires it through its own `sources` — a missing rename or a mistyped path
# would have shipped unnoticed while the lib template stayed green.
readonly TEMPLATES=(rinzler-lib rinzler-binding rinzler-app)

if [[ -z "$(find "$TEMPLATE_ROOT" -name '.template.config' -type d 2>/dev/null)" ]]; then
  log 'no template yet: nothing to regenerate, nothing verified'
  exit 0
fi

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# The repository's name, not the directory's: a worktree is named after its branch,
# and the generated seeds would then carry the branch slug.
name=$(cd "$REPO_ROOT" && basename -s .git "$(git config --get remote.origin.url 2>/dev/null)" 2>/dev/null)
[[ -n "$name" ]] || name=$(basename "$REPO_ROOT")

# Matched as a shell pattern, in which `*` also crosses directory separators, so
# `src/*` is the whole tree under src/.
is_seed() {
  local path=$1 pattern
  for pattern in "${seeds[@]}"; do
    # shellcheck disable=SC2053 # the right-hand side is a pattern on purpose
    [[ "$path" == $pattern ]] && return 0
  done
  return 1
}

# A private template registry, discarded with the scratch directory. Installing into
# the user's registry left one entry per checkout this check ever ran from; once a
# worktree was deleted its entry pointed nowhere, and the stale entries conflicted
# with every later install of the same short names.
hive="$scratch/hive"
run dotnet new install "$TEMPLATE_ROOT" --force --debug:custom-hive "$hive"

divergent=0
for template in "${TEMPLATES[@]}"; do
  seeds_file="$TEMPLATE_ROOT/templates/$template/.template.config/seeds.txt"
  [[ -f "$seeds_file" ]] || fail "$template declares no seeds: $seeds_file"

  seeds=()
  while IFS= read -r pattern; do
    [[ -z "$pattern" || "$pattern" == '#'* ]] && continue
    seeds+=("$pattern")
  done <"$seeds_file"
  ((${#seeds[@]} > 0)) || fail "$template: the seed list is empty, every generated file would be compared"

  output="$scratch/$template"
  run dotnet new "$template" --output "$output" --name "$name" --debug:custom-hive "$hive"

  compared=0
  seeded=0
  diverged_here=0
  while IFS= read -r -d '' generated; do
    relative=${generated#"$output/"}
    committed="$REPO_ROOT/$relative"

    if is_seed "$relative"; then
      seeded=$((seeded + 1))
      continue
    fi

    compared=$((compared + 1))
    if [[ ! -f "$committed" ]]; then
      printf '%s: missing from the repository: %s\n' "$template" "$relative"
      divergent=$((divergent + 1))
      diverged_here=$((diverged_here + 1))
    elif ! cmp -s "$generated" "$committed"; then
      printf '%s: differs from the template: %s\n' "$template" "$relative"
      divergent=$((divergent + 1))
      diverged_here=$((diverged_here + 1))
    fi
  done < <(find "$output" -type f -print0)

  ((compared > 0)) || fail "$template: every generated file was declared a seed, nothing was verified"
  # The count of files compared is not a count of files that matched: a summary that
  # says "42 regenerate byte-identically" above a list of seven that do not is how a
  # reader learns to skim past the summary.
  log "$template: $((compared - diverged_here))/$compared harness file(s) identical, $diverged_here divergent, $seeded seed(s) left to the repository"
done

((divergent == 0)) || fail "$divergent harness file(s) diverge from the template — run ./scripts/_sync-template.sh if the repository is the side that changed"
