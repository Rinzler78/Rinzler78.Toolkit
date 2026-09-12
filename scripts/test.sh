#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

run dotnet test "$REPO_ROOT" --configuration "$CONFIGURATION" --nologo \
  --filter 'Category!=Integration'
