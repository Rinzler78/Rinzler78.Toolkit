#!/usr/bin/env bash
# Copies this repository's harness into the template's shared directory.
#
# The two must be byte-identical — harness-regenerates.sh fails when they are not — and
# keeping them so by hand cost the same mistake twice: an exclude list retyped at the
# prompt, once dropping the two skill READMEs, once deleting the renamed spelling
# config. The list belongs in one place, next to the rename it has to know about.
#
# Not a verb: the facade is what a repository *does*, and this is a chore of the one
# repository that ships the template it uses.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

readonly SHARED="$REPO_ROOT/src/Rinzler78.Templates/templates/shared"

# What the repository owns and the template does not ship as harness: the seeds, this
# repository's own checks, and everything that is not harness at all.
readonly NOT_HARNESS=(
  /.git /.worktrees /graphify-out /openspec /docs /src
  # The scaffold: every template seeds its own tests, named after the generated project.
  /tests
  'obj/' 'bin/' .DS_Store
  /README.md /CLAUDE.md /AGENTS.md '/*.sln' '/*.slnx'
  /Directory.Build.props /Directory.Build.targets /Directory.Packages.props
  # The repository's own words; the harness vocabulary, .cspell/harness.txt, ships.
  /.cspell/project.txt /.cspell.json
  # The history written while Release Please owned releases, frozen at 0.1.0. Later
  # notes live in the GitHub releases; the file belongs to this repository only.
  /CHANGELOG.md
  # Build output. Gitignored, but rsync does not read .gitignore.
  /artifacts
  /scripts/hooks/build-contracts-enforced.sh
  /scripts/hooks/harness-regenerates.sh
  /scripts/_sync-template.sh
)

excludes=()
for entry in "${NOT_HARNESS[@]}"; do excludes+=(--exclude "$entry"); done

run rsync -a --delete "${excludes[@]}" "$REPO_ROOT/" "$SHARED/"

# The spelling configuration ships under a name cspell does not recognise, because a
# .cspell.json living in template content is an active configuration with no dictionary
# beside it — every word under templates/ was unknown until it was renamed. The
# template renames it back on generation.
run cp "$REPO_ROOT/.cspell.json" "$SHARED/cspell-config.json"

log 'the template carries this repository harness'
