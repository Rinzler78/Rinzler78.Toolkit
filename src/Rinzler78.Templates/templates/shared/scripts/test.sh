#!/usr/bin/env bash
# Runs the unit tests, on the agent, never on a device.
#
#   test.sh                 every test project
#   test.sh --staged        only what the staged change can break
#   test.sh --changed [rev] only what this branch changes, against rev
#
# The tests are run by the project's own host rather than through `dotnet test`. On
# the .NET 10 SDK that command offers two paths and neither works here: the VSTest
# bridge is refused outright by Microsoft.Testing.Platform, and the platform runner
# discovers zero tests from an xUnit v3 assembly of this version. The assembly is its
# own runner — it discovered and ran them when asked directly — so that is what the
# facade asks. One layer fewer, and the filters below are the runner's own.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_affected.sh
source "$(dirname "${BASH_SOURCE[0]}")/_affected.sh"

scope=all
base=
while (($#)); do
  case $1 in
    --staged)
      scope=staged
      shift
      ;;
    --changed)
      scope=changed
      [[ -n "${2:-}" && "$2" != --* ]] && {
        base=$2
        shift
      }
      shift
      ;;
    *) fail "usage: $(basename "$0") [--staged | --changed [<revision>]]" ;;
  esac
done

projects=()
case $scope in
  all) while IFS= read -r p; do projects+=("$p"); done < <(affected_test_projects) ;;
  staged)
    files=()
    while IFS= read -r f; do files+=("$f"); done < <(changed_files staged)
    while IFS= read -r p; do projects+=("$p"); done < <(affected_test_projects "${files[@]}")
    ;;
  changed)
    [[ -n "$base" ]] || base=$(integration_branch) || fail 'no integration branch to compare against'
    files=()
    while IFS= read -r f; do files+=("$f"); done < <(changed_files range "$base")
    while IFS= read -r p; do projects+=("$p"); done < <(affected_test_projects "${files[@]}")
    ;;
esac

if ((${#projects[@]} == 0)); then
  # Saying so is the point: "nothing to run" and "everything passed" are different
  # facts, and a loop that confuses them teaches a developer to trust a green that
  # tested nothing.
  log "no test project is affected by this change ($scope)"
  exit 0
fi

log "${#projects[@]} test project(s) selected ($scope)"
for project in "${projects[@]}"; do
  # -trait- excludes a trait; it is the runner's own filter, applied after the `--`
  # that separates host arguments from runner ones. The older spelling, -notrait,
  # still works and prints a deprecation notice on every single run.
  run dotnet run --project "$REPO_ROOT/$project" --configuration "$CONFIGURATION" \
    -- -trait- 'Category=Integration'
done
