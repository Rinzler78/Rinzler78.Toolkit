#!/usr/bin/env bash
# Deploy to an emulator, a simulator or a device. The invocation differs enough per
# platform that encapsulating it is the point of the verb; a repository with no
# application says so rather than pretending.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

log 'this repository ships no application to deploy'
