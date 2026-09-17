#!/usr/bin/env bash
# Measure the build, so that "fast" is a number rather than an impression.
#
# Three points, because they answer three different questions: how long a build
# costs when nothing changed, how long it costs when one project changed, and how
# much of that is restore evaluation rather than compilation. Optimising without
# these three is guessing which one hurts.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly RESULTS="$REPO_ROOT/artifacts/bench"
mkdir -p "$RESULTS"

# A build time measured on a loaded machine says more about the machine than about
# the build. The load is recorded with the numbers, and a clearly loaded machine is
# refused outright: a figure nobody can trust is worse than no figure, because it
# gets quoted.
readonly MAX_LOAD="${MAX_LOAD:-2.0}"
cores=$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1)
load=$(uptime | sed 's/.*averages*: *//' | awk '{print $1}' | tr -d ',')
load_per_core=$(python3 -c "print(f'{float('$load')/max(1,$cores):.2f}')")

log "load average $load over $cores cores ($load_per_core per core)"
if [[ "${FORCE:-}" != "1" ]]; then
  python3 -c "import sys; sys.exit(0 if float('$load_per_core') <= float('$MAX_LOAD') else 1)" ||
    fail "machine too busy to measure ($load_per_core per core, limit $MAX_LOAD). Set FORCE=1 to record anyway, and do not quote the result."
fi

# Second resolution hides the differences this is meant to expose: the first version
# reported three identical figures where a finer clock showed a two-second spread.
# `date +%s%N` is unavailable on BSD, so the clock comes from python.
now() { python3 -c 'import time; print(time.monotonic())'; }

# The build's exit status is preserved: timing a broken build produces numbers that
# look like a measurement and are not one. The end timestamp is still taken, so the
# failure is reported rather than hidden behind an aborted script.
failed=0

elapsed() {
  local start end status
  start=$(now)
  if "$@" >/dev/null 2>&1; then status=0; else status=$?; fi
  end=$(now)
  ((status == 0)) || {
    printf 'build failed (exit %s) during: %s\n' "$status" "$*" >&2
    failed=1
  }
  python3 -c "print(f'{float(\"$end\") - float(\"$start\"):.2f}')"
}

log 'warming up'
run dotnet build "$REPO_ROOT" --nologo >/dev/null

no_change=$(elapsed dotnet build "$REPO_ROOT" --nologo)
no_restore=$(elapsed dotnet build "$REPO_ROOT" --nologo --no-restore)

# A touched project file forces re-evaluation of that project without changing any
# source, which isolates project evaluation from compilation.
touched=$(find "$REPO_ROOT/src" -name '*.csproj' | head -1)
[[ -n "$touched" ]] && touch "$touched"
after_touch=$(elapsed dotnet build "$REPO_ROOT" --nologo)

report="$RESULTS/$(date +%Y-%m-%dT%H-%M-%S).txt"
{
  printf '%-24s %s per core over %s cores\n' 'load average' "$load_per_core" "$cores"
  printf '%-24s %ss\n' 'no change' "$no_change"
  printf '%-24s %ss\n' 'no change, no restore' "$no_restore"
  printf '%-24s %ss\n' 'one project touched' "$after_touch"
} | tee "$report"

((failed == 0)) || fail 'at least one build failed: these numbers measure nothing'
log "recorded in ${report#"$REPO_ROOT/"}"
