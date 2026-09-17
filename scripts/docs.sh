#!/usr/bin/env bash
# Build the API documentation site. A repository with no public surface has no site,
# which is a property of this repository rather than a gap in the facade.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

if [[ -f "$REPO_ROOT/docfx.json" ]]; then
  run dotnet docfx "$REPO_ROOT/docfx.json"
else
  log 'no docfx.json: this repository publishes no assembly with a public surface'
fi
