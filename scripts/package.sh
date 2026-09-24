#!/usr/bin/env bash
# Packs every packable project of the repository.
#
# The version comes from version.txt, which Release Please owns: it is computed from
# Conventional Commits and never edited by hand. A version passed on a command line, or
# written into a project file, would be a second source of truth — and the second one
# is always the one that is wrong.
#
# VERSION_SUFFIX turns a build into a prerelease, which is how `develop` publishes
# nothing that could be mistaken for a release: `0.2.0-develop.41`.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

version=
[[ -f "$REPO_ROOT/version.txt" ]] && version=$(tr -d '[:space:]' <"$REPO_ROOT/version.txt")

arguments=()
if [[ -n "$version" ]]; then
  [[ -n "${VERSION_SUFFIX:-}" ]] && version="$version-$VERSION_SUFFIX"
  arguments+=("-p:Version=$version")
  log "packing version $version"
else
  log 'no version.txt: packing whatever the projects declare'
fi

run dotnet pack "$REPO_ROOT" --configuration Release --nologo \
  --output "$REPO_ROOT/artifacts" ${arguments[@]+"${arguments[@]}"}
