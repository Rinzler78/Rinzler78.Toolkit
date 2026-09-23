#!/usr/bin/env bash
# The facade's mode is recorded in git, and git is where it gets lost: `dotnet new`
# cannot carry a file mode, so a generated repository's scripts arrive as 0644 and
# the initial commit freezes that for every clone. The failure is not local — it
# arrives for the next person, on a fresh checkout, as "permission denied".
# rinzler-stage: pre-commit
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/../_common.sh"

wrong=0
while read -r mode _ _ path; do
  [[ "$mode" == 100755 ]] && continue
  printf 'recorded as %s, must be executable: %s\n' "$mode" "$path"
  wrong=$((wrong + 1))
done < <(cd "$REPO_ROOT" && git ls-files --stage -- '*.sh')

((wrong == 0)) || fail "$wrong script(s) are not executable in the index — run ./scripts/setup-env.sh, or git update-index --chmod=+x <path>"
log 'every tracked script is executable'
