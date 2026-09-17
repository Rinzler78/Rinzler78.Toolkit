#!/usr/bin/env bash
# Restore is locked in continuous integration and permissive locally: a developer
# adding a package must be able to restore it, while a pipeline must fail rather
# than silently accept a lock file that no longer matches the manifest.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

if [[ "${CI:-}" == "true" ]]; then
  run dotnet restore "$REPO_ROOT" --locked-mode
else
  run dotnet restore "$REPO_ROOT"
fi
