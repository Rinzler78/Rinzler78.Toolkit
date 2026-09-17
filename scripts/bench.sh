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

# Second resolution hides the differences this is meant to expose: the first version
# reported three identical figures where a finer clock showed a two-second spread.
# `date +%s%N` is unavailable on BSD, so the clock comes from python.
now() { python3 -c 'import time; print(time.monotonic())'; }

elapsed() {
  local start end
  start=$(now)
  "$@" >/dev/null 2>&1 || true
  end=$(now)
  python3 -c "print(f'{float(\"$end\") - float(\"$start\"):.2f}')"
}

log 'warming up'
dotnet build "$REPO_ROOT" --nologo >/dev/null 2>&1 || true

no_change=$(elapsed dotnet build "$REPO_ROOT" --nologo)
no_restore=$(elapsed dotnet build "$REPO_ROOT" --nologo --no-restore)

# A touched project file forces re-evaluation of that project without changing any
# source, which isolates project evaluation from compilation.
touched=$(find "$REPO_ROOT/src" -name '*.csproj' | head -1)
[[ -n "$touched" ]] && touch "$touched"
after_touch=$(elapsed dotnet build "$REPO_ROOT" --nologo)

report="$RESULTS/$(date +%Y-%m-%dT%H-%M-%S).txt"
{
  printf '%-24s %ss\n' 'no change' "$no_change"
  printf '%-24s %ss\n' 'no change, no restore' "$no_restore"
  printf '%-24s %ss\n' 'one project touched' "$after_touch"
} | tee "$report"

log "recorded in ${report#"$REPO_ROOT/"}"
