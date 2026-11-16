#!/usr/bin/env bash
# Decides whether a tag's version is the one the commits justify, for a package with no
# public API surface — a props/targets package, a template pack.
#
#   _release-version.sh <tag>
#
# Such a package has no surface to diff, so its version is read from the Conventional
# Commits since the last stable tag: `!` or a `BREAKING CHANGE` footer is breaking,
# `feat` a minor, `fix` and `perf` a patch, every other type nothing, the strongest
# effect deciding and a range with none calling for no release. Before 1.0.0 a
# breaking change is a minor, since SemVer declares that API unstable.
#
# The annotated tag's message carries the evidence: the subject of each deciding
# commit, one `- <subject>` line each, as docs/runbooks/nuget-publication.md produces.
#
# 1.0.0 after a 0.x version is never the consequence of a commit: it needs an ADR that
# declares the public surface stable, and the tag's message names its path.
#
# A repository that ships a PublicAPI.Shipped.txt has a surface; its version belongs to
# the surface diff, which is another check. This one leaves it alone and says so.
#
# Runs after _release-tag.sh, which has already fetched the tag from origin.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

warn() { printf '\033[1;33m%s\033[0m %s\n' '??' "$*" >&2; }

# Prints the strongest effect (3 breaking, 2 minor, 1 patch, 0 none, -1 no commit),
# then one line per deciding subject and one per commit that is not conventional.
classify() {
  awk '
    BEGIN { RS = "\036"; max = -1 }
    NF == 0 { next }
    {
      n = split($0, lines, "\n")
      subject = lines[1]
      rank = 0
      if (match(subject, /^[a-z]+(\([^)]*\))?!?: /)) {
        head = substr(subject, 1, RLENGTH)
        type = head
        sub(/[^a-z].*$/, "", type)
        if (head ~ /!: $/) rank = 3
        else if (type == "feat") rank = 2
        else if (type == "fix" || type == "perf") rank = 1
      } else {
        print "UNCONVENTIONAL " subject
      }
      for (i = 2; i <= n; i++) if (lines[i] ~ /^BREAKING[ -]CHANGE: /) rank = 3
      count++
      subjects[count] = subject
      ranks[count] = rank
      if (rank > max) max = rank
    }
    END {
      print "RANK " max
      if (max > 0) for (i = 1; i <= count; i++) if (ranks[i] == max) print "DECIDING " subjects[i]
    }'
}

# The last stable tag reachable from the commit, other than the tag itself.
previous_stable() {
  local commit=$1 tag=$2 candidate
  while IFS= read -r candidate; do
    [[ "$candidate" == "$tag" || "$candidate" == *-* ]] && continue
    printf '%s' "$candidate"
    return 0
  done < <(git -C "$REPO_ROOT" tag --merged "$commit" --list 'v*' --sort=-version:refname)
}

# The tag message must name a file of the tagged commit that declares the surface stable.
require_stability_decision() {
  local commit=$1 message=$2 word
  while IFS= read -r word; do
    git -C "$REPO_ROOT" cat-file -e "$commit:$word" 2>/dev/null || continue
    git -C "$REPO_ROOT" show "$commit:$word" 2>/dev/null | grep -qi 'stable' && return 0
  done < <(printf '%s\n' "$message" | tr -s ' \t' '\n')
  fail "1.0.0 after a 0.x version needs an ADR declaring the public surface stable: name its path in the tag message, and write 'stable' in it"
}

main() {
  local tag=${1:?usage: _release-version.sh <tag>}
  local version=${tag#v}
  local core=${version%%-*}

  git -C "$REPO_ROOT" rev-parse --verify --quiet "refs/tags/$tag" >/dev/null ||
    fail "'$tag' is not a tag of this repository"
  local commit
  commit=$(git -C "$REPO_ROOT" rev-parse "refs/tags/$tag^{commit}")

  if git -C "$REPO_ROOT" ls-tree -r --name-only "$commit" | grep -q 'PublicAPI\.Shipped\.txt$'; then
    log "$tag: this repository has a public surface; its version is judged on the surface diff, not on commits"
    return 0
  fi

  local previous
  previous=$(previous_stable "$commit" "$tag")
  if [[ -z "$previous" ]]; then
    log "$tag: first release — no earlier stable tag to compare with"
    return 0
  fi

  local message
  message=$(git -C "$REPO_ROOT" for-each-ref --format='%(contents:body)' "refs/tags/$tag")

  local pa pb pc
  IFS=. read -r pa pb pc <<<"${previous#v}"

  # 1.0.0 is a decision, whatever the commits say.
  if [[ "$core" == 1.0.0 && "$pa" == 0 ]]; then
    require_stability_decision "$commit" "$message"
    log "$tag: 1.0.0 after $previous, with its stability decision"
    return 0
  fi

  local rank=-1 deciding=() unconventional line
  while IFS= read -r line; do
    case $line in
      'RANK '*) rank=${line#RANK } ;;
      'DECIDING '*) deciding+=("${line#DECIDING }") ;;
      'UNCONVENTIONAL '*) unconventional="${unconventional:-}${line#UNCONVENTIONAL }; " ;;
    esac
  done < <(git -C "$REPO_ROOT" log --no-merges --format='%x1e%B' "$previous..$commit" | classify)
  [[ -z "${unconventional:-}" ]] || warn "not a Conventional Commit, counted as no effect: ${unconventional%; }"

  ((rank > 0)) || fail "no commit since $previous changes the package: no release — $tag has nothing to publish"

  local expected effect
  case $rank in
    3)
      effect='breaking'
      if ((pa >= 1)); then expected="$((pa + 1)).0.0"; else expected="0.$((pb + 1)).0"; fi
      ;;
    2)
      effect='minor'
      expected="$pa.$((pb + 1)).0"
      ;;
    *)
      effect='patch'
      expected="$pa.$pb.$((pc + 1))"
      ;;
  esac

  if [[ "$core" != "$expected" ]]; then
    fail "$tag disagrees with the commits since $previous: a $effect change gives $expected, decided by: ${deciding[*]}"
  fi

  local subject
  for subject in "${deciding[@]}"; do
    grep -qxF -e "- $subject" <<<"$message" ||
      fail "$tag carries no evidence of the commit that decides its version: add the line '- $subject' to the tag message"
  done
  log "$tag: $effect change since $previous, $core as the commits require"
}

[[ "${BASH_SOURCE[0]}" == "$0" ]] && main "$@"
