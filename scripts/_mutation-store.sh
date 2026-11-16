#!/usr/bin/env bash
# Where the mutation score lives between runs: the `badges` branch of the repository.
#
#   _mutation-store.sh fetch <file>   the published scores, written to <file>; nothing is
#                                     written before the first publication
#   _mutation-store.sh publish        artifacts/mutation/ → the branch, as scores.txt and
#                                     mutation.json (the shields.io endpoint)
#
# Not a verb: the nightly publishes and the pull-request job fetches, and neither is a
# thing a developer runs. The branch is an orphan that no integration branch ever merges,
# so writing it is not a change to `develop` or `master` and needs no pull request; the
# score is a measurement, not source. It is written with git plumbing — no checkout, no
# working tree, no branch of the clone touched — and a score that did not change adds no
# commit, so a quiet repository leaves a quiet history.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly BRANCH=badges
readonly RECORDED="$REPO_ROOT/artifacts/mutation"

# Succeeds once the branch is fetched, fails when origin has none. An origin that cannot
# be reached is neither: reading it as "no branch" would switch the no-regression rule
# off silently, on the day the network is down.
fetch_branch() {
  local status=0
  git -C "$REPO_ROOT" ls-remote --exit-code --heads origin "$BRANCH" >/dev/null 2>&1 || status=$?
  case $status in
    0) git -C "$REPO_ROOT" fetch --quiet origin "+refs/heads/$BRANCH:refs/remotes/origin/$BRANCH" ;;
    2) return 1 ;;
    *) fail "cannot reach origin to read the $BRANCH branch (git exit $status)" ;;
  esac
}

fetch_scores() {
  local file=${1:?fetch needs a destination file}
  fetch_branch || {
    log "no $BRANCH branch yet: the first run has no baseline"
    return 0
  }
  mkdir -p "$(dirname "$file")"
  git -C "$REPO_ROOT" show "origin/$BRANCH:scores.txt" >"$file"
  log "baseline read from origin/$BRANCH"
}

publish_scores() {
  [[ -f "$RECORDED/scores.txt" && -f "$RECORDED/badge.json" ]] ||
    fail 'no recorded score to publish: run mutation.sh, without --since, first'

  local parent=() tree scores badge commit
  if fetch_branch; then
    parent=(-p "origin/$BRANCH")
  fi
  scores=$(git -C "$REPO_ROOT" hash-object -w "$RECORDED/scores.txt")
  badge=$(git -C "$REPO_ROOT" hash-object -w "$RECORDED/badge.json")
  tree=$(printf '100644 blob %s\tmutation.json\n100644 blob %s\tscores.txt\n' "$badge" "$scores" |
    git -C "$REPO_ROOT" mktree)

  if ((${#parent[@]} > 0)) && [[ "$(git -C "$REPO_ROOT" rev-parse "origin/$BRANCH^{tree}")" == "$tree" ]]; then
    log 'the score is unchanged: nothing to publish'
    return 0
  fi

  commit=$(git -C "$REPO_ROOT" commit-tree "$tree" ${parent[@]+"${parent[@]}"} -m 'chore: mutation score')
  run git -C "$REPO_ROOT" push origin "$commit:refs/heads/$BRANCH"
}

main() {
  case ${1:-} in
    fetch) fetch_scores "${2:-}" ;;
    publish) publish_scores ;;
    *) fail "usage: $(basename "$0") fetch <file> | publish" ;;
  esac
}

[[ "${BASH_SOURCE[0]}" == "$0" ]] && main "$@"
