#!/usr/bin/env bash
# Mutation testing of every project that owes it, with Stryker.NET.
#
#   mutation.sh                                  the full run: every mutable project, whole
#   mutation.sh --since <rev> [--baseline <file>] the diff run: only what changed since <rev>
#
# A project owes it unless it declares RinzlerCoverageContract Exempt: a binding
# projection produces surviving mutants with no meaning, and a test project is
# Exempt by construction. A repository where nothing owes it runs no mutation at all.
#
# Stryker breaks below 70 %, and 85 % is the target. The diff run also refuses a score
# under the project's own full-run score, read from --baseline (lines of `Project=score`,
# as the full run writes them): the break threshold of that project is the higher of
# the two. A baseline that is absent is the first run, not an error.
#
# Only a full run records anything, in artifacts/mutation/: scores.txt, one line per
# project, and badge.json, a shields.io endpoint carrying the lowest score. The
# nightly publishes both; a diff score describes a patch, not the project.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_affected.sh
source "$(dirname "${BASH_SOURCE[0]}")/_affected.sh"

readonly BREAK_AT=70
readonly TARGET=85
readonly OUTPUT="$REPO_ROOT/artifacts/mutation"

# Projects that owe mutation testing, one per line. The contract is asked of MSBuild,
# not grepped from the project file: a default or an override in a props file counts.
mutable_projects() {
  local project contract
  while IFS= read -r project; do
    _is_test_project "$project" && continue
    contract=$(dotnet msbuild "$REPO_ROOT/$project" -getProperty:RinzlerCoverageContract -nologo | tr -d '[:space:]')
    [[ "$contract" == Exempt ]] || printf '%s\n' "$project"
  done < <(_all_projects)
}

baseline_of() {
  local name=$1 file=$2
  [[ -f "$file" ]] || return 0
  awk -F= -v name="$name" '$1 == name { print $2; exit }' "$file"
}

# The higher of the break threshold and the project's full-run score.
break_for() {
  local baseline=$1
  awk -v floor="$BREAK_AT" -v baseline="${baseline:-0}" 'BEGIN { print (baseline + 0 > floor + 0) ? baseline : floor }'
}

mutate() {
  local project=$1 since=$2 baseline_file=$3
  local name break_at status=0 score
  name=$(basename "$project" .csproj)
  break_at=$(break_for "$(baseline_of "$name" "$baseline_file")")

  # No --test-project: the repository has a solution, so Stryker works in its context,
  # finds the test projects there, and refuses one named on the command line.
  local arguments=(--test-runner mtp --project "$project" --break-at "$break_at"
    --threshold-high "$TARGET" --threshold-low "$BREAK_AT" --output "$OUTPUT/stryker")
  [[ -n "$since" ]] && arguments+=("--since:$since")

  log "dotnet stryker ${arguments[*]}"
  (cd "$REPO_ROOT" && dotnet stryker "${arguments[@]}") >"$OUTPUT/$name.log" 2>&1 || status=$?
  cat "$OUTPUT/$name.log"
  ((status == 0)) || fail "$name is under its mutation threshold of $break_at %, or Stryker failed (exit $status)"

  # Stryker prints no score when nothing was mutated: an empty diff is not a failure.
  # And a diff score describes a patch, so only the full run records one.
  score=$(sed -n 's/.*final mutation score is \([0-9.]*\).*/\1/p' "$OUTPUT/$name.log" | tail -n 1)
  [[ -n "$score" && -z "$since" ]] && printf '%s=%s\n' "$name" "$score" >>"$OUTPUT/scores.txt"
  return 0
}

write_badge() {
  local lowest=$1 color=yellow
  awk -v s="$lowest" -v t="$TARGET" 'BEGIN { exit !(s + 0 >= t + 0) }' && color=brightgreen
  printf '{"schemaVersion":1,"label":"mutation","message":"%s%%","color":"%s"}\n' "$lowest" "$color" >"$OUTPUT/badge.json"
}

main() {
  local since='' baseline=''
  while (($#)); do
    case $1 in
      --since) since=${2:?--since needs a revision} && shift 2 ;;
      --baseline) baseline=${2:?--baseline needs a file} && shift 2 ;;
      *) fail "usage: $(basename "$0") [--since <revision> [--baseline <file>]]" ;;
    esac
  done

  rm -rf "$OUTPUT"
  mkdir -p "$OUTPUT"
  run dotnet tool restore

  local projects=() project
  while IFS= read -r project; do [[ -n "$project" ]] && projects+=("$project"); done < <(mutable_projects)
  ((${#projects[@]} > 0)) || {
    log 'no project to mutate: every project is Exempt, or the repository has none'
    return 0
  }

  for project in "${projects[@]}"; do
    mutate "$project" "$since" "$baseline"
  done

  [[ -z "$since" && -f "$OUTPUT/scores.txt" ]] || return 0
  write_badge "$(cut -d= -f2 "$OUTPUT/scores.txt" | sort -n | head -n 1)"
  log "scores written to ${OUTPUT#"$REPO_ROOT"/}"
}

[[ "${BASH_SOURCE[0]}" == "$0" ]] && main "$@"
