#!/usr/bin/env bash
# Packs every packable project of the repository.
#
# The version comes from the tags, through MinVer, and from nothing else: a tagged
# commit packs the tag's version, any other commit the next patch as a prerelease —
# 0.1.1-alpha.0.3, three commits after v0.1.0 — so a build that is not a release cannot
# be mistaken for one. The same version is computed in an IDE, here and in the release
# workflow. A version passed on a command line, or written into a project file, would
# be a second source of truth, and the second one is always the one that is wrong.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

run dotnet pack "$REPO_ROOT" --configuration Release --nologo --output "$REPO_ROOT/artifacts"
