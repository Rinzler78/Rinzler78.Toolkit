#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

# No user interface in this repository. The verb stays for facade uniformity.
log 'no end-to-end surface in this repository'
