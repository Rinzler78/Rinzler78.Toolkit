#!/usr/bin/env bash
# The fast inner loop. Not a lighter build — the same build, re-triggered on change,
# which is what actually shortens the cycle. A second build semantics reserved for
# developers is how "it works on my machine" is manufactured.
#
# `dotnet watch` wants a project file: unlike `dotnet build` it does not accept a
# directory or a solution, and passing one fails with "Could not find a MSBuild
# project file".
#
# `--tests` watches the test project the current change reaches and re-runs it on
# every save. That is the loop test-driven development actually needs: a red bar
# within a second of writing the assertion, without running a suite that has nothing
# to do with the change.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_affected.sh
source "$(dirname "${BASH_SOURCE[0]}")/_affected.sh"

target=
watch_tests=0
while (($#)); do
  case $1 in
    --tests)
      watch_tests=1
      shift
      ;;
    *)
      target=$1
      shift
      ;;
  esac
done

if ((watch_tests)); then
  base=$(integration_branch) || base=
  files=()
  if [[ -n "$base" ]]; then
    while IFS= read -r f; do files+=("$f"); done < <(changed_files range "$base")
  fi
  selection=$(affected_test_projects ${files[@]+"${files[@]}"} | head -1)
  [[ -n "$selection" ]] || fail 'no test project is reachable from this change; name one explicitly'
  log "watching ${selection}"
  run dotnet watch --project "$REPO_ROOT/$selection" run -- -trait- 'Category=Integration'
  exit 0
fi

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
