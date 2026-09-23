#!/usr/bin/env bash
# Which test projects a change can possibly break.
#
# Running the whole suite on every save is what makes a developer stop running it,
# and a test-driven loop that costs a coffee is not a loop. So the selection is
# computed instead of guessed: from the changed files, to the projects that own them,
# to every project that references those transitively, down to the test projects in
# that closure.
#
# It is deliberately *over*-inclusive, never under: a change to anything shared —
# the build manifests, the toolchain pin, the facade itself — selects everything,
# because a wrong selection that runs too little is a green light that means nothing.
#
# Sourced, not executed: it defines affected_test_projects and prints nothing.

# Files that can change the meaning of any project in the repository.
_is_global_change() {
  case $1 in
    Directory.Build.props | Directory.Build.targets | Directory.Packages.props) return 0 ;;
    global.json | nuget.config | *.slnx | *.sln) return 0 ;;
    packages.lock.json | */packages.lock.json) return 0 ;;
    scripts/* | .github/* | .config/* | .pre-commit-config.yaml | mise.toml) return 0 ;;
    src/Rinzler78.Build/*) return 0 ;;
    *) return 1 ;;
  esac
}

_owning_project() {
  # The nearest project file at or above the changed file.
  local dir
  dir=$(dirname "$1")
  while [[ "$dir" != "." && "$dir" != "/" ]]; do
    local found
    found=$(find "$REPO_ROOT/$dir" -maxdepth 1 -name '*.csproj' -print -quit 2>/dev/null)
    [[ -n "$found" ]] && {
      printf '%s\n' "${found#"$REPO_ROOT"/}"
      return 0
    }
    dir=$(dirname "$dir")
  done
  return 1
}

# Every project *this repository builds* — which is not every .csproj on disk. A
# template pack ships project files as content, and a repository that packages
# templates would otherwise try to test its own template material, where central
# package management does not even apply (NU1010).
_all_projects() {
  local solution
  solution=$(find "$REPO_ROOT" -maxdepth 1 \( -name '*.slnx' -o -name '*.sln' \) -print -quit)
  if [[ -n "$solution" ]]; then
    (cd "$REPO_ROOT" && dotnet sln "$solution" list 2>/dev/null |
      grep -E '\.csproj$' | tr '\134' '/' | sort)
    return 0
  fi
  (cd "$REPO_ROOT" && find src tests -name '*.csproj' -type f 2>/dev/null | sort)
}

_is_test_project() {
  # A project is a test project because it says so, not because of where it sits: the
  # directory convention is a habit, the property is a fact the build already uses.
  grep -q '<IsTestProject>true</IsTestProject>' "$REPO_ROOT/$1"
}

# Every test project in the repository.
all_test_projects() {
  local project
  for project in $(_all_projects); do
    _is_test_project "$project" && printf '%s\n' "$project"
  done
}

# Prints, one per line, the test projects affected by the given changed files.
# With no argument, prints every test project.
affected_test_projects() {
  local changed=("$@") project

  if (($# == 0)); then
    all_test_projects
    return 0
  fi

  # A newline-delimited string rather than an array: bash 3.2 — what macOS ships —
  # treats the expansion of an empty array as an unbound variable under `set -u`, and
  # an empty selection is the normal case for a documentation-only change.
  local selected=''
  local file
  for file in "${changed[@]}"; do
    if _is_global_change "$file"; then
      log 'a shared file changed: every test project is selected' >&2
      all_test_projects
      return 0
    fi
    project=$(_owning_project "$file") || continue
    _contains "$selected" "$project" || selected+="$project"$'\n'
  done

  # Walk the reference graph outwards until it stops growing: a change in a project
  # reaches every project that depends on it, at any depth.
  local grew=1 candidate references reference resolved
  while ((grew)); do
    grew=0
    for candidate in $(_all_projects); do
      _contains "$selected" "$candidate" && continue
      references=$(grep -o 'ProjectReference Include="[^"]*"' "$REPO_ROOT/$candidate" |
        sed 's/.*Include="//; s/"$//' | tr '\\134' '/')
      for reference in $references; do
        resolved=$(_resolve "$candidate" "$reference") || continue
        _contains "$selected" "$resolved" && {
          selected+="$candidate"$'\n'
          grew=1
          break
        }
      done
    done
  done

  while IFS= read -r project; do
    [[ -n "$project" ]] || continue
    _is_test_project "$project" && printf '%s\n' "$project"
  done <<<"$selected" | sort -u
}

_contains() {
  [[ -n "$1" ]] && printf '%s' "$1" | grep -qxF "$2"
}

# A ProjectReference path, as a path from the repository root.
_resolve() {
  local from=$1 reference=$2 directory
  directory=$(cd "$REPO_ROOT/$(dirname "$from")/$(dirname "$reference")" 2>/dev/null && pwd) || return 1
  printf '%s\n' "${directory#"$REPO_ROOT"/}/$(basename "$reference")"
}

# The changed files, as paths relative to the repository root.
# $1: staged | range
# $2: the base revision, for a range
changed_files() {
  case $1 in
    staged) (cd "$REPO_ROOT" && git diff --cached --name-only --diff-filter=ACMR) ;;
    range)
      local base=$2 merge_base
      # No merge base means the comparison is impossible, not that nothing changed.
      # Returning an empty list here would have selected nothing while announcing the
      # opposite: the caller reads a non-zero status as "compare against everything".
      merge_base=$(cd "$REPO_ROOT" && git merge-base HEAD "$base" 2>/dev/null) || return 1
      (
        cd "$REPO_ROOT" && git diff --name-only --diff-filter=ACMR "$merge_base" HEAD
        git diff --name-only --diff-filter=ACMR
        git diff --cached --name-only --diff-filter=ACMR
      ) | sort -u
      ;;
  esac
}

# The branch a feature branch is cut from, whatever this repository calls it.
integration_branch() {
  local candidate
  for candidate in origin/develop develop origin/master master origin/main main; do
    (cd "$REPO_ROOT" && git rev-parse --verify --quiet "$candidate" >/dev/null) && {
      printf '%s\n' "$candidate"
      return 0
    }
  done
  return 1
}
