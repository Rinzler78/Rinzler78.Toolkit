#!/usr/bin/env bash
# Every repository check in scripts/hooks/, discovered rather than enumerated.
#
# The inventory used to be written twice — once in .pre-commit-config.yaml, once in
# the CI workflow — and the two copies drifted immediately: build-contracts-enforced
# and harness-regenerates ran in CI and nowhere else, so a developer learnt about a
# broken contract from a runner instead of from a commit. One list, several runners.
#
# A check declares when it runs, in its own header, and the stage is chosen by what
# the check costs and what it protects:
#
#     # rinzler-stage: pre-commit    fast and offline — it runs on every save-commit
#     # rinzler-stage: pre-push      slow, or needs the network — the branch is leaving
#     # rinzler-stage: pre-merge     integration-level — two histories are becoming one
#
# The order is a ratchet, not a repetition: a pre-commit check never waits for the
# network, and a pre-merge check may take minutes because merging is not a loop.
#
# A check that declares nothing is refused rather than skipped: silently running
# nothing is the failure mode this whole harness exists to prevent.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly HOOK_DIR="$REPO_ROOT/scripts/hooks"
readonly STAGES=(pre-commit pre-push pre-merge)

supported() {
  local IFS='|'
  echo "${STAGES[*]}"
}

usage() {
  printf 'usage: %s [--stage %s|all]\n' "$(basename "$0")" "$(supported)" >&2
  exit 2
}

stage=all
while (($#)); do
  case "$1" in
    --stage)
      [[ -n "${2:-}" ]] || usage
      stage=$2
      shift 2
      ;;
    --stage=*)
      stage=${1#--stage=}
      shift
      ;;
    -h | --help) usage ;;
    *) usage ;;
  esac
done

[[ "$stage" == all || " ${STAGES[*]} " == *" $stage "* ]] || usage

declared_stage() {
  sed -n 's/^# rinzler-stage:[[:space:]]*\([a-z-]*\).*/\1/p' "$1" | head -1
}

# The whole directory is validated before anything runs, whatever stage was asked
# for: an undeclared check must not be able to hide behind a stage filter.
declare -a selected=()
undeclared=0
for hook in "$HOOK_DIR"/*.sh; do
  [[ -f "$hook" ]] || continue
  name=$(basename "$hook")
  [[ "$name" == _* ]] && continue

  declared=$(declared_stage "$hook")
  if [[ -z "$declared" ]]; then
    printf 'scripts/hooks/%s declares no stage: add "# rinzler-stage: %s"\n' \
      "$name" "$(supported)"
    undeclared=$((undeclared + 1))
    continue
  elif [[ " ${STAGES[*]} " != *" $declared "* ]]; then
    # An unsupported value is a different mistake from a missing line, and a
    # diagnosis that names the wrong one sends the reader to the wrong place.
    printf 'scripts/hooks/%s declares an unsupported stage: %s (expected %s)\n' \
      "$name" "$declared" "$(supported)"
    undeclared=$((undeclared + 1))
    continue
  fi

  [[ "$stage" == all || "$declared" == "$stage" ]] && selected+=("$hook")
done

((undeclared == 0)) || fail "$undeclared check(s) do not declare a usable stage"

if ((${#selected[@]} == 0)); then
  # A stage with nothing in it is a fact about this repository, not a mistake: the
  # whole directory has just been validated, so nothing is hiding. Saying it out loud
  # is what distinguishes it from a run that checked something.
  log "no check declares stage: $stage"
  exit 0
fi

# fail_fast is false by design: a developer wants every problem in one pass, not the
# first one repeated after each fix.
failed=()
for hook in "${selected[@]}"; do
  log "${hook#"$REPO_ROOT"/}"
  "$hook" || failed+=("${hook#"$REPO_ROOT"/}")
done

((${#failed[@]} == 0)) || fail "$(printf '%s ' "failed:" "${failed[@]}")"
log "${#selected[@]} check(s) passed"
