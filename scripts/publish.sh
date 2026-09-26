#!/usr/bin/env bash
# Pushes the packed artefacts to nuget.org.
#
# EXPECTED_VERSION, when set, is the version the release claims. Every artefact must
# carry it, or nothing is pushed: a push is irreversible — nuget.org does not allow a
# version to be replaced — so the one moment to check that the tag and the packages
# agree is *before* the push, not in a post-mortem.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

shopt -s nullglob
packages=("$REPO_ROOT"/artifacts/*.nupkg)
((${#packages[@]} > 0)) || fail 'no package to publish: run ./scripts/package.sh first'

if [[ -n "${EXPECTED_VERSION:-}" ]]; then
  for package in "${packages[@]}"; do
    name=$(basename "$package" .nupkg)
    [[ "$name" == *".$EXPECTED_VERSION" ]] || fail "$name is not version $EXPECTED_VERSION — the tag and the packages disagree"
  done
  log "${#packages[@]} package(s), all at version $EXPECTED_VERSION"
fi

[[ -n "${NUGET_API_KEY:-}" ]] || fail 'NUGET_API_KEY is not set'

run dotnet nuget push "${packages[@]}" \
  --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" \
  --skip-duplicate
