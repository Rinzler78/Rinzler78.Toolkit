#!/usr/bin/env bash
# Refuses the gestures that would defeat a blocking check. Denial rules in
# settings.json cover the literal forms; this catches the composed ones.
set -Eeuo pipefail
payload=$(cat)
command=$(printf '%s' "$payload" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_input",{}).get("command",""))')

deny() {
  printf '%s\n' "$1" >&2
  exit 2
}

case "$command" in
  *--no-verify*) deny 'A blocking hook signals a root cause. Fix it; do not bypass it.' ;;
  *SKIP=*) deny 'Skipping a pre-commit hook is not available in this repository.' ;;
  *"git push"*--force*) deny 'Force pushing is refused: history is linear and shared.' ;;
esac
exit 0
