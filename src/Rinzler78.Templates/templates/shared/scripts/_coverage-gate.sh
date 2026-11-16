#!/usr/bin/env bash
# The coverage gate: what a cobertura report must show for the projects that owe it.
#
#   _coverage-gate.sh evaluate <report> [--since <revision>]
#
# Each project declares RinzlerCoverageContract, and the gate reads it from MSBuild, so a
# default or an override in a props file counts:
#
#   Enforced   90 % of lines and 90 % of branches
#   Reduced    the floor the project declares in RinzlerCoverageFloor, for lines and
#              branches alike. The contract lowers the bar and never removes the gate; a
#              Reduced project with no usable floor fails rather than passing unchecked.
#   Exempt     no gate at all
#
# With --since, the lines a project changed since <revision> must be covered at 95 % or
# more, Enforced and Reduced alike: new untested code is refused without punishing
# trivial code that was already there. A line the report does not list has no sequence
# point (a brace, a comment): there is nothing to cover and it is not counted.
#
# A test project is never gated, and neither is a project shipping no assembly
# (IncludeBuildOutput false). A project with an assembly that the report never mentions
# fails: absence of data is not absence of risk.
#
# Not a verb: `coverage.sh --gate` collects, then calls this. It is callable alone on a
# report that already exists, which is how it is tested.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_affected.sh
source "$(dirname "${BASH_SOURCE[0]}")/_affected.sh"

readonly ENFORCED_FLOOR=90
readonly DIFF_FLOOR=95

failures=0
problem() {
  printf '\033[1;31m%s\033[0m %s\n' '!!' "$*" >&2
  failures=$((failures + 1))
}

property() { dotnet msbuild "$REPO_ROOT/$1" "-getProperty:$2" -nologo | tr -d '[:space:]'; }

# True when $1 is a plain number from 0 to 100.
is_percent() {
  [[ "$1" =~ ^[0-9]+(\.[0-9]+)?$ ]] && awk -v v="$1" 'BEGIN { exit !(v + 0 <= 100) }'
}

# "<lines> <branches>" in percent for an assembly of the report, nothing when absent.
# The report is read as a stream of tags, so that its layout (one line or many) is
# irrelevant. An assembly reported several times — a project tested on several target
# frameworks, or loaded by several modules — is held to its worst entry: the figure that
# flatters is not the one that protects.
package_rates() {
  awk -v assembly="$2" '
    function attribute(tag, key,   at) {
      if (!match(tag, " " key "=\"[^\"]*\"")) return ""
      at = substr(tag, RSTART + length(key) + 3, RLENGTH - length(key) - 4)
      return at
    }
    BEGIN { RS = "<" }
    /^package / && attribute($0, "name") == assembly {
      lines = attribute($0, "line-rate") * 100
      branches = attribute($0, "branch-rate") * 100
      if (!found || lines < worst_lines) worst_lines = lines
      if (!found || branches < worst_branches) worst_branches = branches
      found = 1
    }
    END { if (found) printf "%.2f %.2f\n", worst_lines, worst_branches }' "$1"
}

below() { awk -v value="$1" -v floor="$2" 'BEGIN { exit !(value + 0 < floor + 0) }'; }

gate_project() {
  local report=$1 project=$2 name contract floor assembly rates lines branches before=$failures
  name=$(basename "$project" .csproj)
  contract=$(property "$project" RinzlerCoverageContract)
  case ${contract:-Enforced} in
    Exempt)
      log "$name: Exempt, not gated"
      return 0
      ;;
    Enforced) floor=$ENFORCED_FLOOR ;;
    Reduced)
      floor=$(property "$project" RinzlerCoverageFloor)
      is_percent "$floor" || {
        problem "$name is Reduced and declares no usable RinzlerCoverageFloor (a number from 0 to 100; found '$floor')"
        return 0
      }
      ;;
    *)
      problem "$name declares an unknown RinzlerCoverageContract: $contract"
      return 0
      ;;
  esac

  [[ "$(property "$project" IncludeBuildOutput)" == false ]] && {
    log "$name: ships no assembly, not gated"
    return 0
  }

  assembly=$(property "$project" AssemblyName)
  rates=$(package_rates "$report" "${assembly:-$name}")
  [[ -n "$rates" ]] || {
    problem "$name: no coverage recorded for assembly ${assembly:-$name}: no test reaches it, or the report is not of this repository"
    return 0
  }
  read -r lines branches <<<"$rates"
  below "$lines" "$floor" && problem "$name: $lines % of lines covered, the floor is $floor %"
  below "$branches" "$floor" && problem "$name: $branches % of branches covered, the floor is $floor %"
  ((failures > before)) || log "$name: $lines % of lines, $branches % of branches, floor $floor %"
}

# "<file> TAB <number>" for every line added or changed since <revision>, in C# sources.
# Renames are not followed, so that a file renamed and edited is a new file with all its
# lines; names are NUL-separated, so that a space or an accent in one is not a quoting
# problem.
changed_lines() {
  local since=$1 file
  while IFS= read -r -d '' file; do
    git -C "$REPO_ROOT" diff -U0 --no-color --no-renames "$since...HEAD" -- "$file" |
      awk -v file="$file" '
        /^@@/ {
          match($0, /\+[0-9]+(,[0-9]+)?/)
          range = substr($0, RSTART + 1, RLENGTH - 1)
          split(range, part, ",")
          count = (part[2] == "") ? 1 : part[2]
          for (i = 0; i < count; i++) printf "%s\t%d\n", file, part[1] + i
        }'
  done < <(git -C "$REPO_ROOT" diff -z --name-only --no-renames --diff-filter=AM "$since...HEAD" -- '*.cs')
}

# What the report says of the changed lines, in two kinds of record:
#   F <file> <coverable changed lines> <covered>   for each changed file it knows
#   P <assembly> <classes> <classes under this checkout>   for each assembly
# A filename is read as the machine that collected the report saw it: under the checkout
# as typed or as resolved (a symlink, /private/var on macOS), with `\` or `/`, or as a
# deterministic `/_/` path. The P records are what tells a report of another checkout
# from a report with nothing to cover.
diff_by_file() {
  local report=$1 lines_file=$2 physical
  physical=$(cd "$REPO_ROOT" && pwd -P)
  # Two inputs, two record separators: the changed lines are read by line, then the
  # report by tag, which the RS= operand between them switches.
  # The two roots are separate variables: any separator inside one string is a character
  # a path may contain, and a root split in two matches nothing and fails open.
  awk -F'\t' -v typed="$REPO_ROOT/" -v resolved="$physical/" '
    function relative(path) {
      gsub(/\\/, "/", path)
      if (index(path, typed) == 1) return substr(path, length(typed) + 1)
      if (index(path, resolved) == 1) return substr(path, length(resolved) + 1)
      if (index(path, "/_/") == 1) return substr(path, 4)
      return ""
    }
    FILENAME == ARGV[1] { changed[$1 SUBSEP $2] = 1; next }
    {
      if ($0 ~ /^package /) {
        match($0, / name="[^"]*"/)
        package = substr($0, RSTART + 7, RLENGTH - 8)
        packages[package] = 1
      } else if ($0 ~ /^class /) {
        match($0, / filename="[^"]*"/)
        file = relative(substr($0, RSTART + 11, RLENGTH - 12))
        classes[package]++
        if (file != "") matched[package]++
      } else if ($0 ~ /^line / && file != "") {
        match($0, / number="[0-9]+"/)
        number = substr($0, RSTART + 9, RLENGTH - 10)
        match($0, / hits="[0-9]+"/)
        hits = substr($0, RSTART + 7, RLENGTH - 8) + 0
        key = file SUBSEP number
        if (!(key in seen) || hits > seen[key]) seen[key] = hits
      }
    }
    END {
      for (key in seen) {
        if (!(key in changed)) continue
        split(key, part, SUBSEP)
        total[part[1]]++
        if (seen[key] > 0) covered[part[1]]++
      }
      for (f in total) printf "F\t%s\t%d\t%d\n", f, total[f], covered[f] + 0
      for (p in packages) printf "P\t%s\t%d\t%d\n", p, classes[p] + 0, matched[p] + 0
    }' "$lines_file" RS='<' "$report"
}

# The project that owns <file>, when it owes the gate: not a test project, not Exempt.
gated_owner() {
  local project contract
  project=$(_owning_project "$1") || return 1
  _is_test_project "$project" && return 1
  contract=$(property "$project" RinzlerCoverageContract)
  [[ "${contract:-Enforced}" == Exempt ]] && return 1
  printf '%s\n' "$project"
}

gate_diff() {
  local report=$1 since=$2 lines_file facts per_project file total covered project name percent assembly classes matched kind
  lines_file=$(mktemp)
  facts=$(mktemp)
  per_project=$(mktemp)
  # shellcheck disable=SC2064 # the paths are fixed now, on purpose
  trap "rm -f '$lines_file' '$facts' '$per_project'" RETURN
  changed_lines "$since" >"$lines_file"
  [[ -s "$lines_file" ]] || {
    log "no C# line changed since $since: no diff to cover"
    return 0
  }
  diff_by_file "$report" "$lines_file" >"$facts"

  # The projects the diff touches that owe the gate, each with its changed files.
  local touched=()
  while IFS= read -r file; do
    project=$(gated_owner "$file") || continue
    [[ " ${touched[*]-} " == *" $project "* ]] || touched+=("$project")
  done < <(cut -f1 "$lines_file" | sort -u)

  while IFS=$'\t' read -r kind file total covered; do
    [[ "$kind" == F ]] || continue
    project=$(gated_owner "$file") || continue
    printf '%s %s %s\n' "$project" "$total" "$covered"
  done <"$facts" >"$per_project"

  # A report that names no file of a touched project's assembly under this checkout is
  # not a report of this checkout: reading it as "nothing to cover" would pass anything.
  for project in ${touched[@]+"${touched[@]}"}; do
    name=$(basename "$project" .csproj)
    assembly=$(property "$project" AssemblyName)
    while IFS=$'\t' read -r kind file classes matched; do
      [[ "$kind" == P && "$file" == "${assembly:-$name}" ]] || continue
      ((classes > 0 && matched == 0)) &&
        problem "$name: the report names no file under this checkout ($classes classes elsewhere): it is not of this checkout"
    done <"$facts"
  done

  while read -r project total covered; do
    name=$(basename "$project" .csproj)
    percent=$(awk -v t="$total" -v c="$covered" 'BEGIN { printf "%.2f", c * 100 / t }')
    if below "$percent" "$DIFF_FLOOR"; then
      problem "$name: $percent % of the changed lines are covered ($covered of $total), the diff floor is $DIFF_FLOOR %"
    else
      log "$name: diff covered at $percent % ($covered of $total)"
    fi
  done < <(awk '{ total[$1] += $2; covered[$1] += $3 } END { for (p in total) print p, total[p], covered[p] }' "$per_project")
}

evaluate() {
  local report=${1:?evaluate needs a report} since=''
  shift
  while (($#)); do
    case $1 in
      --since) since=${2:?--since needs a revision} && shift 2 ;;
      *) fail "usage: $(basename "$0") evaluate <report> [--since <revision>]" ;;
    esac
  done
  [[ -f "$REPO_ROOT/$report" || -f "$report" ]] || fail "coverage report not found: $report"
  [[ -f "$report" ]] || report="$REPO_ROOT/$report"

  local project
  while IFS= read -r project; do
    _is_test_project "$project" && continue
    gate_project "$report" "$project"
  done < <(_all_projects)

  [[ -n "$since" ]] && gate_diff "$report" "$since"
  ((failures == 0)) || fail "$failures coverage gate failure(s)"
  log 'the coverage gate holds'
}

main() {
  case ${1:-} in
    evaluate) shift && evaluate "$@" ;;
    *) fail "usage: $(basename "$0") evaluate <report> [--since <revision>]" ;;
  esac
}

# An `if`, not `[[ ]] && main`: sourced, the test is false and the line would end the
# sourcing script with status 1 under set -e.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then main "$@"; fi
