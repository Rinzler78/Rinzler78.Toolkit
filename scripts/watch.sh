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

# The effective output type, not the literal text: a project can be runnable through
# WinExe, or inherit Exe from its SDK without ever spelling it out, and a raw grep
# sends both down the build branch where watch never launches anything.
output_type=$(dotnet msbuild "$target" -getProperty:OutputType -nologo 2>/dev/null | tr -d '[:space:]')

case "$output_type" in
  Exe | WinExe)
    run dotnet watch --project "$target" run
    ;;
  *)
    log "not a runnable project (OutputType=${output_type:-unset}): watching its build"
    run dotnet watch --project "$target" build
    ;;
esac
