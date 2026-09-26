#!/usr/bin/env bash
# Pushes the packed artefacts to nuget.org.
#
#   publish.sh [--expect <version>] [--verify]
#
# --expect is the version the release claims. Every artefact must declare it in its
# manifest, or nothing is pushed: a push is irreversible — nuget.org does not allow a
# version to be replaced — so the one moment to check that the tag and the packages
# agree is *before* the push, not in a post-mortem. The manifest, not the file name,
# is what nuget.org reads: a file renamed to the right version still publishes the
# version its manifest declares.
#
# --verify stops after the checks. The release workflow runs it before it requests
# the publishing credential, so that a mismatch never gets as far as a key.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

expected=
verify_only=false
while (($# > 0)); do
  case "$1" in
    --expect) expected=${2:?--expect needs a version} && shift 2 ;;
    --verify) verify_only=true && shift ;;
    *) fail "unknown argument: $1" ;;
  esac
done

shopt -s nullglob
packages=("$REPO_ROOT"/artifacts/*.nupkg)
((${#packages[@]} > 0)) || fail 'no package to publish: run ./scripts/package.sh first'

if [[ -n "$expected" ]]; then
  for package in "${packages[@]}"; do
    # XML, not lines: the element may be indented or split, so the manifest is
    # flattened before the version element is read.
    declared=$(unzip -p "$package" '*.nuspec' | tr -d '\r\n' |
      sed -n 's:.*<version>[[:space:]]*\([^<[:space:]]*\)[[:space:]]*</version>.*:\1:p')
    [[ "$declared" == "$expected" ]] ||
      fail "$(basename "$package") declares version ${declared:-none}, not $expected — the tag and the packages disagree"
  done
  log "${#packages[@]} package(s), all at version $expected"
fi

$verify_only && exit 0

[[ -n "${NUGET_API_KEY:-}" ]] || fail 'NUGET_API_KEY is not set'

run dotnet nuget push "${packages[@]}" \
  --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" \
  --skip-duplicate
