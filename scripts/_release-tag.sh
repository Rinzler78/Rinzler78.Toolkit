#!/usr/bin/env bash
# Decides whether a pushed tag may publish, and which version it publishes.
#
# Pushing a tag is the only act that publishes, and nuget.org never lets a version be
# replaced: every refusal below is cheaper here than after the push. A tag publishes
# only if it is
#   - named vMAJOR.MINOR.PATCH, optionally -alpha.N, -beta.N or -rc.N — the labels the
#     NuGet documentation defines, numbered after a dot so that rc.10 sorts after rc.2;
#   - annotated, because only an annotated tag carries a signature of its own;
#   - on a commit origin/master contains, because master is where releases are cut and
#     the pull request into it is where the costly checks run;
#   - signed, as GitHub verifies it: the verdict comes from the forge that holds the
#     keys, not from a keyring on the runner.
#
# Prints version=<version without v> and prerelease=<true|false>, and appends them to
# $GITHUB_OUTPUT when the workflow provides one.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

tag=${1:?usage: _release-tag.sh <tag>}

readonly NUMBER='(0|[1-9][0-9]*)'
readonly FORM="^v$NUMBER\\.$NUMBER\\.$NUMBER(-(alpha|beta|rc)\\.$NUMBER)?\$"
[[ $tag =~ $FORM ]] ||
  fail "'$tag' is not a release tag: expected vMAJOR.MINOR.PATCH, optionally followed by -alpha.N, -beta.N or -rc.N"

[[ $(git -C "$REPO_ROOT" cat-file -t "refs/tags/$tag") == tag ]] ||
  fail "'$tag' is a lightweight tag: tag with --annotate --sign so that it carries a signature"

commit=$(git -C "$REPO_ROOT" rev-parse "refs/tags/$tag^{commit}")
git -C "$REPO_ROOT" merge-base --is-ancestor "$commit" origin/master ||
  fail "'$tag' points at a commit origin/master does not contain: promote it through a pull request first"

object=$(git -C "$REPO_ROOT" rev-parse "refs/tags/$tag")
verification=$(gh api "repos/${GITHUB_REPOSITORY:?}/git/tags/$object" --jq '.verification')
[[ $(jq -r '.verified' <<<"$verification") == true ]] ||
  fail "'$tag' carries no verified signature: $(jq -r '.reason' <<<"$verification")"

version=${tag#v}
prerelease=false
[[ $version == *-* ]] && prerelease=true

printf 'version=%s\nprerelease=%s\n' "$version" "$prerelease" |
  tee -a "${GITHUB_OUTPUT:-/dev/null}"
