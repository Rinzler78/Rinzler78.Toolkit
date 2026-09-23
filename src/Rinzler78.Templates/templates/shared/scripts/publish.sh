#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

[[ -n "${NUGET_API_KEY:-}" ]] || fail 'NUGET_API_KEY is not set'
run dotnet nuget push "$REPO_ROOT/artifacts/*.nupkg" \
  --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" \
  --skip-duplicate
