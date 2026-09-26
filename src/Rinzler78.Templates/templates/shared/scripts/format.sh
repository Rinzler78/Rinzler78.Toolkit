#!/usr/bin/env bash
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

# Whitespace and style, never analyser fixes: the analysers belong to the build,
# which alone applies diagnostic suppressors, and an automatic fix for a
# suppressed diagnostic rewrites code the compiler accepts.
run dotnet format whitespace "$REPO_ROOT" --verbosity minimal
run dotnet format style "$REPO_ROOT" --verbosity minimal
