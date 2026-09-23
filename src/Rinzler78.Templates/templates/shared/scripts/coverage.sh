#!/usr/bin/env bash
# Coverage of the unit suite, collected around the test host rather than by a VSTest
# data collector: there is no VSTest in this chain any more, so `--collect 'XPlat
# Code Coverage'` would silently collect nothing. dotnet-coverage wraps the process
# instead, which works for any runner.
#
# Accepts the same selection arguments as test.sh, so a coverage figure can be taken
# for a change without paying for the whole repository.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly OUTPUT="$REPO_ROOT/artifacts/coverage"
mkdir -p "$OUTPUT"

run dotnet tool restore
# Through `dotnet`, not as a bare command: a local tool lives in the manifest, not on
# PATH, and calling it directly works only where someone happens to have installed it
# globally — the opposite of a self-contained harness.
run dotnet dotnet-coverage collect --output "$OUTPUT/coverage.cobertura.xml" --output-format cobertura \
  -- "$REPO_ROOT/scripts/test.sh" "$@"
log "coverage written to ${OUTPUT#"$REPO_ROOT"/}/coverage.cobertura.xml"
