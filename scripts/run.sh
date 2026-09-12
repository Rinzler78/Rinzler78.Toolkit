#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

# The toolkit ships no runnable application: Build is props/targets, Templates is
# a `dotnet new` pack. The verb exists so that the facade is uniform.
log 'nothing to run in this repository'
