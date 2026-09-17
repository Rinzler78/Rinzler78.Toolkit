#!/usr/bin/env bash
# The fast inner loop. Not a lighter build — the same build, re-triggered on change,
# which is what actually shortens the cycle. A second build semantics reserved for
# developers is how "it works on my machine" is manufactured.
#
# `dotnet watch` wants a project file: unlike `dotnet build` it does not accept a
# directory or a solution, and passing one fails with "Could not find a MSBuild
# project file".
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

target=${1:-}

if [[ -z "$target" ]]; then
  target=$(find "$REPO_ROOT/src" -name '*.csproj' -print 2>/dev/null | sort | head -1)
fi

[[ -n "$target" ]] || fail 'no project to watch; pass one as the first argument'

if grep -q '<OutputType>Exe</OutputType>' "$target"; then
  run dotnet watch --project "$target" run
else
  log "no runnable project: watching the build of $(basename "$target")"
  run dotnet watch --project "$target" build
fi
