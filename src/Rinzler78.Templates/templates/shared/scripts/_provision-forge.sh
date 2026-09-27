#!/usr/bin/env bash
# Applies the forge settings a repository of this ecosystem needs, and that no file in
# the tree can carry: settings live in the forge, and a generated repository inherits
# none of them. Idempotent — run it at creation, and again whenever this file changes.
#
#   bash scripts/_provision-forge.sh <owner>/<repository>
#
# - Workflows get a read-only token and cannot approve pull requests: no workflow of
#   the harness writes to the repository except the release, which asks for it.
# - The `release` environment admits tags `v*` only. A nuget.org trusted publishing
#   policy matches the workflow's file name and this environment, never the ref, and
#   GitHub silently creates an unprotected environment the first time a job names one.
# - develop takes squashed pull requests, with a linear, signed history.
# - Copilot reviews every push to a pull request into either branch, so that the review
#   never depends on someone remembering to request it: WORKFLOW.md's review loop ends
#   only when it finds nothing.
# - master takes merge commits only: a promotion keeps develop's history, so the next
#   promotion shows only what is new. Signed, but not linear, by construction.
# - Tags `v*` can be neither moved nor deleted: nuget.org never replaces a version, so a
#   moved tag would name code other than the package it published. Only repository
#   admins create them.
#
# Required checks are the jobs of .github/workflows/ci.yml, which the harness owns and
# keeps byte-identical in every repository. Each is bound to GitHub Actions, app 15368:
# a check required by name alone is satisfied by any integration posting that name.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

repo=${1:?usage: _provision-forge.sh <owner>/<repository>}
readonly CHECKS='[{"context":"verify","integration_id":15368},{"context":"lint","integration_id":15368}]'
# master also requires promotion.yml's check, which runs from master's own copy of the
# workflow, so that a pull request cannot rewrite the gate it is judged by.
readonly MASTER_CHECKS='[{"context":"verify","integration_id":15368},{"context":"lint","integration_id":15368},{"context":"promotion-source","integration_id":15368}]'

run gh api -X PUT "repos/$repo/actions/permissions/workflow" \
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=false >/dev/null

jq -nc '{deployment_branch_policy: {protected_branches: false, custom_branch_policies: true}}' |
  run gh api -X PUT "repos/$repo/environments/release" --input - >/dev/null
# Reconciled, not appended: a policy left from an earlier setup — `master`, before
# releases became tags — would keep admitting what this script claims to exclude.
policies=$(gh api "repos/$repo/environments/release/deployment-branch-policies")
for stale in $(jq -r '.branch_policies[] | select(.name != "v*" or .type != "tag") | .id' <<<"$policies"); do
  run gh api -X DELETE "repos/$repo/environments/release/deployment-branch-policies/$stale" >/dev/null
done
if ! jq -e '.branch_policies[] | select(.name == "v*" and .type == "tag")' <<<"$policies" >/dev/null; then
  run gh api -X POST "repos/$repo/environments/release/deployment-branch-policies" \
    -f 'name=v*' -f type=tag >/dev/null
fi

branch_ruleset() {
  local branch=$1 method=$2 linear=$3 checks=$4
  jq -nc --arg branch "$branch" --arg method "$method" --argjson linear "$linear" \
    --argjson checks "$checks" '{
      name: $branch, target: "branch", enforcement: "active", bypass_actors: [],
      conditions: {ref_name: {include: ["refs/heads/\($branch)"], exclude: []}},
      rules: ([{type: "deletion"}, {type: "non_fast_forward"}, {type: "required_signatures"}]
        + (if $linear then [{type: "required_linear_history"}] else [] end)
        + [{type: "pull_request", parameters: {
              required_approving_review_count: 0, dismiss_stale_reviews_on_push: false,
              require_code_owner_review: false, require_last_push_approval: false,
              required_review_thread_resolution: false, allowed_merge_methods: [$method]}},
           {type: "required_status_checks", parameters: {
              strict_required_status_checks_policy: false, required_status_checks: $checks}},
           {type: "copilot_code_review", parameters: {
              review_on_push: true, review_draft_pull_requests: true}}])
    }'
}

# Two rulesets, because a bypass applies to a whole ruleset. Release tags can be
# neither moved nor deleted, by anyone: no bypass. Only repository admins create one —
# the tag check verifies that a tag is signed, not who signed it, so a signature alone
# must not be enough to publish. RepositoryRole 5 is the admin role.
tag_immutability_ruleset() {
  jq -nc '{
    name: "release tags", target: "tag", enforcement: "active", bypass_actors: [],
    conditions: {ref_name: {include: ["refs/tags/v*"], exclude: []}},
    rules: [{type: "update"}, {type: "deletion"}]
  }'
}

tag_creation_ruleset() {
  jq -nc '{
    name: "release tag creation", target: "tag", enforcement: "active",
    bypass_actors: [{actor_id: 5, actor_type: "RepositoryRole", bypass_mode: "always"}],
    conditions: {ref_name: {include: ["refs/tags/v*"], exclude: []}},
    rules: [{type: "creation"}]
  }'
}

existing=$(gh api "repos/$repo/rulesets")
apply() {
  local body=$1 name id
  name=$(jq -r '.name' <<<"$body")
  id=$(jq -r --arg name "$name" '.[] | select(.name == $name) | .id' <<<"$existing")
  if [[ -n "$id" ]]; then
    run gh api -X PUT "repos/$repo/rulesets/$id" --input - <<<"$body" >/dev/null
  else
    run gh api -X POST "repos/$repo/rulesets" --input - <<<"$body" >/dev/null
  fi
}

apply "$(branch_ruleset develop squash true "$CHECKS")"
apply "$(branch_ruleset master merge false "$MASTER_CHECKS")"
apply "$(tag_immutability_ruleset)"
apply "$(tag_creation_ruleset)"

log "forge settings applied to $repo"
