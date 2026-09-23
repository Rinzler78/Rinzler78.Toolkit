#!/usr/bin/env bash
# The toolkit's own verification: its harness must be reproducible, byte for byte,
# from the template it ships. Until the template exists this check has nothing to
# compare, and says so rather than reporting a success it did not earn.
# rinzler-stage: pre-push
# Installing the template pack and regenerating a repository is too slow for a
# commit, and too important to leave to CI alone.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly TEMPLATE_ROOT="$REPO_ROOT/src/Rinzler78.Templates"

if [[ -z "$(find "$TEMPLATE_ROOT" -name '.template.config' -type d 2>/dev/null)" ]]; then
  log 'no template yet: nothing to regenerate, nothing verified'
  exit 0
fi

scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

run dotnet new install "$TEMPLATE_ROOT" --force
run dotnet new rinzler-lib --output "$scratch/generated" --name Rinzler78.Toolkit

divergent=0
while IFS= read -r -d '' generated; do
  relative=${generated#"$scratch/generated/"}
  committed="$REPO_ROOT/$relative"
  if [[ ! -f "$committed" ]]; then
    printf 'missing from the repository: %s\n' "$relative"
    divergent=$((divergent + 1))
  elif ! cmp -s "$generated" "$committed"; then
    printf 'differs from the template: %s\n' "$relative"
    divergent=$((divergent + 1))
  fi
done < <(find "$scratch/generated" -type f -print0)

((divergent == 0)) || fail "$divergent file(s) diverge from the template"
log 'harness regenerates byte-identically'
