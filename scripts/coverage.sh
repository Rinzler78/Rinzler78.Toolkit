#!/usr/bin/env bash
# Coverage of the unit suite, collected around the test host rather than by a VSTest
# data collector: there is no VSTest in this chain any more, so `--collect 'XPlat
# Code Coverage'` would silently collect nothing. dotnet-coverage wraps the process
# instead, which works for any runner.
#
# Accepts the same selection arguments as test.sh, so a coverage figure can be taken
# for a change without paying for the whole repository.
#
#   coverage.sh                       collect, and report the figure
#   coverage.sh --gate                collect, then hold each project to its contract
#   coverage.sh --gate --since <rev>  and the lines changed since <rev> to the diff floor
#
# The gate is what a pull request runs: the floors and the diff floor are in
# _coverage-gate.sh, which this sources rather than spawns.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_coverage-gate.sh
source "$(dirname "${BASH_SOURCE[0]}")/_coverage-gate.sh"

gate=false
since=''
selection=()
while (($#)); do
  case $1 in
    --gate) gate=true && shift ;;
    --since) since=${2:?--since needs a revision} && shift 2 ;;
    *) selection+=("$1") && shift ;;
  esac
done
[[ -z "$since" || "$gate" == true ]] || fail '--since only means something with --gate'

readonly OUTPUT="$REPO_ROOT/artifacts/coverage"
mkdir -p "$OUTPUT"

run dotnet tool restore
# Through `dotnet`, not as a bare command: a local tool lives in the manifest, not on
# PATH, and calling it directly works only where someone happens to have installed it
# globally — the opposite of a self-contained harness.
run dotnet dotnet-coverage collect --output "$OUTPUT/coverage.cobertura.xml" --output-format cobertura \
  -- "$REPO_ROOT/scripts/test.sh" ${selection[@]+"${selection[@]}"}
log "coverage written to ${OUTPUT#"$REPO_ROOT"/}/coverage.cobertura.xml"

if [[ "$gate" == true ]]; then
  evaluate "$OUTPUT/coverage.cobertura.xml" ${since:+--since "$since"}
fi
