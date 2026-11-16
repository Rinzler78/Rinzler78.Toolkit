#!/usr/bin/env bash
# Shared preamble. Every verb sources this: strict mode, repository root, and a
# single place to change how commands are echoed.
# Sourced by every verb and by the helpers a verb sources: the second time is a no-op,
# because REPO_ROOT and CONFIGURATION are read-only once declared.
[[ -n "${_RINZLER_COMMON_LOADED:-}" ]] && return 0
_RINZLER_COMMON_LOADED=1

set -Eeuo pipefail

# These two are the interface of this file: the verbs that source it read them, and
# the child processes they launch inherit them. Exported rather than merely global,
# because that is what they are.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Debug}"
declare -rx REPO_ROOT CONFIGURATION

log() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
fail() {
  printf '\033[1;31m%s\033[0m %s\n' '!!' "$*" >&2
  exit 1
}

run() {
  log "$*"
  "$@"
}
