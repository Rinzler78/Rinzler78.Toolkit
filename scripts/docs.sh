#!/usr/bin/env bash
# Build the API documentation site.
#
# DocFX is a local .NET tool declared in .config/dotnet-tools.json, not a command
# assumed to be on the path: a consumer following setup-env must reach a tool that
# exists. `dotnet tool restore` materialises it from the manifest.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

if [[ ! -f "$REPO_ROOT/docfx.json" ]]; then
  log 'no docfx.json: this repository publishes no assembly with a public surface'
  exit 0
fi

run dotnet tool restore
run dotnet docfx "$REPO_ROOT/docfx.json"
