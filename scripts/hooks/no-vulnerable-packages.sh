#!/usr/bin/env bash
# Fails on any known-vulnerable transitive or direct package. The advisory database
# is queried over the network: that is a legitimate network use, unlike fetching
# harness content.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

report=$(cd "$REPO_ROOT" && dotnet list package --vulnerable --include-transitive 2>&1)
printf '%s\n' "$report"
if grep -qE '^\s+>' <<<"$report"; then
  fail 'vulnerable packages found — raise the version, do not add an ignore entry'
fi
log 'no vulnerable package'
