#!/usr/bin/env bash
# The trimming measurement of every Committed library: what its trimming test
# application weighs once published trimmed, with the library marked and without.
#
#   trimming.sh                 measure, and write artifacts/trimming/summary.md
#   trimming.sh --write         and record the sizes in the README
#   trimming.sh --check         and compare them with what the README records
#   trimming.sh ... --aot       and publish each application natively, ahead of time
#
# A project owes this when it declares RinzlerTrimContract Committed. Its application is
# tests/<Project>.Trimming/<Project>.Trimming.csproj by convention, and it must publish
# with TrimMode partial: the trimmer's default since .NET 8 is full, which trims every
# assembly whether it opted in or not and would show the library no benefit. Partial is
# what Blazor WebAssembly and iOS do, so it is what an integrator gets.
#
# --aot publishes the application with RinzlerAot=true, which its project turns into
# PublishAot. It is not PublishAot itself on the command line: a global property flows
# to every project the application references, and a library that also targets
# netstandard2.0 cannot be compiled ahead of time in that slice (NETSDK1207).
#
# The "unmarked" figure is the same application with the library's contract overridden to
# OutOfScope on the command line, which an MSBuild global property wins over the project
# file. The application fixes its own runtime identifier (linux-x64) and is self-contained,
# so that a figure written on a laptop is the figure a runner measures: the library is IL
# and its size does not depend on the host, but the framework around it does. It is the
# application's project that says so, and not this script's command line, because a
# runtime identifier on the command line flows to every project the application references
# and rewrites their lock files: the application belongs to the solution, and its own lock
# file covers the runtime it names. A publication restores variants of the application
# (unmarked, native) and rewrites the lock files for them, and NuGet offers no way to
# forbid that while a lock file exists (NU1005): the lock files are put back as they were
# found, whatever the outcome, because a lock rewritten by a measurement no longer matches
# the repository's own locked restore.
#
# --aot cannot cross operating systems: the application takes the host's runtime
# identifier when RinzlerAot is true, and a native build needs that host's toolchain
# (clang, and zlib headers on Linux).
#
# What is compared is the marked library, in bytes. The README may be a little off (5 %),
# never more: a bigger library than the one recorded is a regression, and a promotion pull
# request refuses it. A library whose marking trims nothing has no contract worth the
# name, and fails too. The published payload is recorded as information and gates nothing:
# 5 % of a framework of megabytes is a hundred times a library of kilobytes, so a payload
# gate would pass any regression of the library and trip on a runtime patch.
#
# The size of a WASM payload or an IPA belongs to the head that builds one. This measures
# the trimmed managed payload, which is what the library controls.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"
# shellcheck source=scripts/_coverage-gate.sh
source "$(dirname "${BASH_SOURCE[0]}")/_coverage-gate.sh"

readonly TOLERANCE_PERCENT=5
readonly START='<!-- trimming:start -->'
readonly END='<!-- trimming:end -->'
readonly OUTPUT="$REPO_ROOT/artifacts/trimming"
readonly README="$REPO_ROOT/README.md"

# The lock files as they stand, and as they were put back: a measurement leaves the
# repository the way it found it.
snapshot_locks() {
  locks_snapshot="$OUTPUT/locks"
  mkdir -p "$locks_snapshot"
  local file
  while IFS= read -r -d '' file; do
    mkdir -p "$locks_snapshot/$(dirname "$file")"
    cp "$REPO_ROOT/$file" "$locks_snapshot/$file"
  done < <(cd "$REPO_ROOT" && find . -name packages.lock.json -type f -not -path './artifacts/*' -print0)
}

restore_locks() {
  local file
  while IFS= read -r -d '' file; do
    [[ -f "$locks_snapshot/$file" ]] || rm -f "$REPO_ROOT/$file"
  done < <(cd "$REPO_ROOT" && find . -name packages.lock.json -type f -not -path './artifacts/*' -print0)
  while IFS= read -r -d '' file; do
    cp "$locks_snapshot/$file" "$REPO_ROOT/$file"
  done < <(cd "$locks_snapshot" && find . -name packages.lock.json -type f -print0)
  rm -rf "$locks_snapshot"
}

# Every project that owes the measurement: not a test project, shipping an assembly, and
# declaring Committed.
committed_projects() {
  local project
  while IFS= read -r project; do
    _is_test_project "$project" && continue
    [[ "$(property "$project" IncludeBuildOutput)" == false ]] && continue
    # An `if`: as the last command of the loop, a `&&` that finds nothing would end the
    # function with status 1, and `set -e` the script with it, without a word.
    if [[ "$(property "$project" RinzlerTrimContract)" == Committed ]]; then printf '%s\n' "$project"; fi
  done < <(_all_projects)
}

file_bytes() { wc -c <"$1" | tr -d '[:space:]'; }

# The managed payload: every assembly in a publication directory.
dll_bytes() {
  local total=0 file
  while IFS= read -r -d '' file; do
    total=$((total + $(file_bytes "$file")))
  done < <(find "$1" -name '*.dll' -type f -print0)
  printf '%s\n' "$total"
}

# The application of <project>, by convention, relative to the repository.
trimming_app() {
  local name
  name=$(basename "$1" .csproj)
  printf 'tests/%s.Trimming/%s.Trimming.csproj\n' "$name" "$name"
}

# Publishes the application of <project> trimmed, into <directory>, any further argument
# going to `dotnet publish`.
publish_trimmed() {
  local app=$1 directory=$2
  shift 2
  run dotnet publish "$app" --configuration Release --output "$directory" --nologo "$@"
}

# Appends "<name> <library marked> <library unmarked> <published marked> <published
# unmarked>" to $rows, or records why not.
measure_project() {
  local project=$1 name assembly app marked unmarked
  name=$(basename "$project" .csproj)
  assembly=$(property "$project" AssemblyName)
  assembly=${assembly:-$name}
  app=$(trimming_app "$project")
  [[ -f "$REPO_ROOT/$app" ]] || {
    problem "$name is Committed and has no trimming test application: expected $app"
    return 0
  }
  marked="$OUTPUT/$name/marked"
  unmarked="$OUTPUT/$name/unmarked"
  rm -rf "$marked" "$unmarked"
  publish_trimmed "$app" "$marked" || {
    problem "$name: the trimming application does not publish trimmed, library marked"
    return 0
  }
  publish_trimmed "$app" "$unmarked" -p:RinzlerTrimContract=OutOfScope || {
    problem "$name: the trimming application does not publish trimmed, library unmarked"
    return 0
  }
  # A publication without the assembly is a different problem from a library that trims
  # nothing, and a size read from a file that is not there is not a size.
  local directory
  for directory in "$marked" "$unmarked"; do
    [[ -f "$directory/$assembly.dll" ]] || {
      problem "$name: $assembly.dll is missing from the publication in $directory: the assembly name is not the one published, or the trimmer removed it"
      return 0
    }
  done
  printf '%s %s %s %s %s\n' "$name" \
    "$(file_bytes "$marked/$assembly.dll")" "$(file_bytes "$unmarked/$assembly.dll")" \
    "$(dll_bytes "$marked")" "$(dll_bytes "$unmarked")" >>"$rows"
}

# A library whose marking trims nothing has made a promise that changes nothing.
check_benefit() {
  local name library_marked library_unmarked
  while read -r name library_marked library_unmarked _ _; do
    ((library_marked < library_unmarked)) ||
      problem "$name: marking the library trims nothing ($library_marked bytes marked, $library_unmarked unmarked)"
  done <"$rows"
}

# The block as it stands in the README, markers included.
render_block() {
  local name library_marked library_unmarked published_marked published_unmarked
  printf '%s\n' "$START"
  printf '| Project | Library, marked | Library, unmarked | Published, marked | Published, unmarked |\n'
  printf '|---|---:|---:|---:|---:|\n'
  while read -r name library_marked library_unmarked published_marked published_unmarked; do
    printf '| %s | %s | %s | %s | %s |\n' "$name" "$library_marked" "$library_unmarked" "$published_marked" "$published_unmarked"
  done <"$rows"
  printf '%s\n' "$END"
}

# The README's own block, found whatever its line endings: a checkout on Windows turns
# the markers into "<marker>\r", which no exact comparison would ever match.
has_block() { tr -d '\r' <"$README" | grep -qxF "$START"; }

# The README's own block replaced where it stands, or appended when it has none, and one
# block only, however many there were. What the author wrote around it is never touched.
write_block() {
  local block=$1 result="$OUTPUT/README.new"
  [[ -f "$README" ]] || fail "README.md not found: nowhere to record the sizes"
  if has_block; then
    awk -v start="$START" -v end="$END" -v blockfile="$block" '
      { line = $0; sub(/\r$/, "", line) }
      line == start { if (!written) { while ((getline text < blockfile) > 0) print text; close(blockfile); written = 1 } skipping = 1; next }
      skipping && line == end { skipping = 0; next }
      !skipping { print }' "$README" >"$result"
  else
    {
      cat "$README"
      [[ -s "$README" ]] && printf '\n'
      cat "$block"
    } >"$result"
  fi
  cat "$result" >"$README"
}

# One cell of the README's block for a project, "#" in front so that an empty cell is not
# taken for a missing row, and nothing at all when there is no row. <column> counts awk
# fields: 3 is the marked library, 5 the marked payload.
recorded_cell() {
  awk -F'|' -v start="$START" -v end="$END" -v name="$1" -v column="$2" '
    { sub(/\r$/, "") }
    $0 == start && !done { inside = 1; next }
    $0 == end && inside { inside = 0; done = 1 }
    inside && NF >= 7 {
      for (i = 2; i <= 6; i++) gsub(/^[ \t]+|[ \t]+$/, "", $i)
      if ($2 == name) { print "#" $column; exit }
    }' "$README"
}

# More than the tolerance above what was recorded.
grew() { ((($1) * 100 > ($2) * (100 + TOLERANCE_PERCENT))); }

check_block() {
  local name library_marked recorded
  has_block ||
    {
      problem "README.md records no trimming measurement: run scripts/trimming.sh --write"
      return 0
    }
  while read -r name library_marked _ _ _; do
    recorded=$(recorded_cell "$name" 3)
    [[ -n "$recorded" ]] || {
      problem "$name is not in the trimming block of README.md: run scripts/trimming.sh --write"
      continue
    }
    recorded=${recorded#\#}
    [[ "$recorded" =~ ^[0-9]+$ ]] || {
      problem "$name: README.md records no size for the marked library (found '$recorded'): run scripts/trimming.sh --write"
      continue
    }
    grew "$library_marked" "$recorded" &&
      problem "$name: the marked library weighs $library_marked bytes, $recorded recorded: more than $TOLERANCE_PERCENT % over"
  done <"$rows"
  return 0
}

# Publishes each application natively, which is the proof the library is AOT compatible
# in use and not only on paper, and records the size of what comes out.
publish_aot() {
  local project name app assembly directory
  while IFS= read -r project; do
    name=$(basename "$project" .csproj)
    app=$(trimming_app "$project")
    [[ -f "$REPO_ROOT/$app" ]] || continue
    directory="$OUTPUT/$name/aot"
    rm -rf "$directory"
    run dotnet publish "$app" --configuration Release --output "$directory" \
      --nologo -p:RinzlerAot=true || {
      problem "$name: cannot be compiled ahead-of-time"
      continue
    }
    assembly=$(property "$app" AssemblyName)
    assembly=${assembly:-$name.Trimming}
    [[ -f "$directory/$assembly" ]] || {
      problem "$name: the native publication produced no $assembly"
      continue
    }
    printf '%s %s\n' "$name" "$(file_bytes "$directory/$assembly")" >>"$aot_rows"
  done <<<"$committed"
}

# What a pull request posts: the sizes, what the README records beside them when the run
# compares, and the verdict.
write_summary() {
  local mode=$1 name library_marked library_unmarked published_marked published_unmarked bytes recorded_library recorded_payload
  {
    printf '<!-- trimming-summary -->\n### Trimming\n\nManaged payload of each trimming test application, in bytes.\n\n'
    if [[ $mode == compare ]]; then
      printf '| Project | Library, marked | Library, unmarked | Published, marked | Published, unmarked | Recorded: library, marked | Recorded: published, marked |\n'
      printf '|---|---:|---:|---:|---:|---:|---:|\n'
    else
      printf '| Project | Library, marked | Library, unmarked | Published, marked | Published, unmarked |\n'
      printf '|---|---:|---:|---:|---:|\n'
    fi
    while read -r name library_marked library_unmarked published_marked published_unmarked; do
      printf '| %s | %s | %s | %s | %s |' "$name" "$library_marked" "$library_unmarked" "$published_marked" "$published_unmarked"
      if [[ $mode == compare ]]; then
        recorded_library=$(recorded_cell "$name" 3)
        recorded_payload=$(recorded_cell "$name" 5)
        printf ' %s | %s |' "${recorded_library#\#}" "${recorded_payload#\#}"
      fi
      printf '\n'
    done <"$rows"
    if [[ -s "$aot_rows" ]]; then
      printf '\n| Project | Native, ahead of time |\n|---|---:|\n'
      while read -r name bytes; do
        printf '| %s | %s |\n' "$name" "$bytes"
      done <"$aot_rows"
    fi
    if ((failures == 0)); then
      printf '\nThe trimming gate holds.\n'
    else
      printf '\n**%s trimming failure(s)**, named in the job log.\n' "$failures"
    fi
  } >"$OUTPUT/summary.md"
}

usage() { fail "usage: $(basename "$0") [--write | --check] [--aot]"; }

main() {
  local mode=measure aot=false
  while (($#)); do
    case $1 in
      --write)
        [[ $mode == measure ]] || usage
        mode=record
        ;;
      --check)
        [[ $mode == measure ]] || usage
        mode=compare
        ;;
      --aot) aot=true ;;
      *) usage ;;
    esac
    shift
  done

  committed=$(committed_projects)
  [[ -n "$committed" ]] || {
    log 'no Committed library: nothing to measure'
    return 0
  }

  # Everything this writes, scratch files included, is under $OUTPUT, which the next run
  # wipes: nothing is left in a temporary directory by a run that failed half way.
  rm -rf "$OUTPUT"
  mkdir -p "$OUTPUT"
  snapshot_locks
  trap restore_locks EXIT
  rows="$OUTPUT/rows.txt"
  aot_rows="$OUTPUT/aot.txt"
  : >"$rows"
  : >"$aot_rows"

  local project
  while IFS= read -r project; do
    measure_project "$project"
  done <<<"$committed"
  check_benefit

  if ((failures == 0)); then
    case $mode in
      record)
        local block
        block="$OUTPUT/block.md"
        render_block >"$block"
        write_block "$block"
        log 'trimming sizes recorded in README.md'
        ;;
      compare) check_block ;;
    esac
  fi
  if [[ $aot == true ]]; then publish_aot; fi

  write_summary "$mode"
  ((failures == 0)) || fail "$failures trimming failure(s)"
  log 'the trimming gate holds'
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then main "$@"; fi
