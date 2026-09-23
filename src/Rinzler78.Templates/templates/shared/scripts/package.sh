#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

run dotnet pack "$REPO_ROOT" --configuration Release --nologo \
  --output "$REPO_ROOT/artifacts"
