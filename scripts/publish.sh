#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

[[ -n "${NUGET_API_KEY:-}" ]] || fail 'NUGET_API_KEY is not set'
# The directory is quoted, the filename glob is not: quoting the whole argument passes
# the literal string `artifacts/*.nupkg` to NuGet, which finds no such file.
# shellcheck disable=SC2086 # the glob must reach the shell, and the path has no spaces
run dotnet nuget push "$REPO_ROOT"/artifacts/*.nupkg \
  --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY" \
  --skip-duplicate
