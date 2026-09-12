#!/usr/bin/env bash
# Formats what was just written, so that the pre-commit formatter never reports a
# diff the agent could have avoided.
set -Eeuo pipefail
payload=$(cat)
path=$(printf '%s' "$payload" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_input",{}).get("file_path",""))')
[[ -n "$path" && -f "$path" ]] || exit 0
case "$path" in
  *.cs | *.csproj | *.props | *.targets) dotnet format --include "$path" --verbosity quiet 2>/dev/null || true ;;
  *.sh) command -v shfmt >/dev/null 2>&1 && shfmt -i 2 -ci -w "$path" || true ;;
esac
exit 0
