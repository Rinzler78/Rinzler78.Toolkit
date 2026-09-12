#!/usr/bin/env bash
# Dependency freshness is blocking, but measured as unattended drift rather than
# instantaneously: an automated bump needs time to land, and failing the moment an
# upstream releases would deadlock against the update bot.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

readonly MAX_DRIFT_DAYS="${MAX_DRIFT_DAYS:-14}"

report=$(cd "$REPO_ROOT" && dotnet list package --outdated 2>&1)
printf '%s\n' "$report"

if ! grep -qE '^\s+>' <<<"$report"; then
  log 'no outdated package'
  exit 0
fi

# A bump pull request open longer than the drift budget is the failure, not the
# existence of a newer version.
stale=$(gh pr list --repo "$(gh repo view --json nameWithOwner -q .nameWithOwner)" \
  --label dependencies --state open \
  --json createdAt -q "[.[] | select((now - (.createdAt|fromdateiso8601)) > ($MAX_DRIFT_DAYS * 86400))] | length" 2>/dev/null || echo 0)

if [[ "${stale:-0}" -gt 0 ]]; then
  fail "$stale dependency pull request(s) open for more than $MAX_DRIFT_DAYS days"
fi
log "outdated packages present, none drifting beyond $MAX_DRIFT_DAYS days"
