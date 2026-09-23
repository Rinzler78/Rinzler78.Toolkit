#!/usr/bin/env bash
# Every repository check in scripts/hooks/, discovered rather than enumerated.
#
# The inventory used to be written twice — once in .pre-commit-config.yaml, once in
# the CI workflow — and the two copies drifted immediately: build-contracts-enforced
# and harness-regenerates ran in CI and nowhere else, so a developer learnt about a
# broken contract from a runner instead of from a commit. One list, several runners.
#
# A check declares when it runs, in its own header:
#
#     # rinzler-stage: pre-commit    fast and offline
#     # rinzler-stage: pre-push      slow, or needs the network
#
# A check that declares nothing is refused rather than skipped: silently running
# nothing is the failure mode this whole harness exists to prevent.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly HOOK_DIR="$REPO_ROOT/scripts/hooks"
readonly STAGES=(pre-commit pre-push)

usage() {
  printf 'usage: %s [--stage pre-commit|pre-push|all]\n' "$(basename "$0")" >&2
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
  if [[ " ${STAGES[*]} " != *" $declared "* ]]; then
    printf 'scripts/hooks/%s declares no stage: add "# rinzler-stage: %s"\n' \
      "$name" "$(
        IFS='|'
        echo "${STAGES[*]}"
      )"
    undeclared=$((undeclared + 1))
    continue
  fi

  [[ "$stage" == all || "$declared" == "$stage" ]] && selected+=("$hook")
done

((undeclared == 0)) || fail "$undeclared check(s) declare no stage"
((${#selected[@]} > 0)) || fail "no check to run for stage: $stage"

# fail_fast is false by design: a developer wants every problem in one pass, not the
# first one repeated after each fix.
failed=()
for hook in "${selected[@]}"; do
  log "${hook#"$REPO_ROOT"/}"
  "$hook" || failed+=("${hook#"$REPO_ROOT"/}")
done

((${#failed[@]} == 0)) || fail "$(printf '%s ' "failed:" "${failed[@]}")"
log "${#selected[@]} check(s) passed"
