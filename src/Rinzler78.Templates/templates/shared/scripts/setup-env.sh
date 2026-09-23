#!/usr/bin/env bash
# Converges the machine to the declared toolchain, or — with --check — reports what
# is missing without touching anything. --check is a CI gate, so it must never
# mutate and must exit non-zero on the first unmet requirement.
# shellcheck source=scripts/_common.sh
source "$(dirname "${BASH_SOURCE[0]}")/_common.sh"

CHECK_ONLY=0
[[ "${1:-}" == "--check" ]] && CHECK_ONLY=1

missing=0
need() {
  local tool=$1 hint=$2
  if command -v "$tool" >/dev/null 2>&1; then
    printf '  %-12s %s\n' "$tool" "$(command -v "$tool")"
  else
    printf '  %-12s MISSING — %s\n' "$tool" "$hint"
    missing=$((missing + 1))
  fi
}

# The script facade has to be executable, and that mode is part of the harness rather
# than an accident of whoever created the files: `dotnet new` cannot carry a file
# mode, so a freshly generated repository arrives with a facade nobody can run, and
# its first commit would record 100644 for every verb. A check refuses that commit;
# this verb restores the mode.
log 'script modes'
# readarray is bash 4; macOS ships bash 3.2 and the harness has to run where the
# developer is, not where the runner is.
not_executable=()
while IFS= read -r script; do
  not_executable+=("$script")
done < <(find "$REPO_ROOT/scripts" "$REPO_ROOT/.claude/hooks" \
  -name '*.sh' -type f ! -perm -u+x -print 2>/dev/null | sort)

if ((${#not_executable[@]} > 0)); then
  for script in "${not_executable[@]}"; do
    printf '  %s is not executable\n' "${script#"$REPO_ROOT"/}"
  done
  if ((CHECK_ONLY)); then
    missing=$((missing + ${#not_executable[@]}))
  else
    run chmod +x "${not_executable[@]}"
  fi
else
  printf '  %-12s every script is executable\n' 'mode'
fi

log 'declared toolchain'
need mise 'https://mise.jdx.dev — per-directory activation, nothing global'
need dotnet 'https://dotnet.microsoft.com/download'
need git 'xcode-select --install, or your package manager'
need pre-commit 'pipx install pre-commit'

# The SDK band matters more than the presence of a dotnet binary: a newer preview
# installed system-wide silently drives workload resolution unless global.json pins
# the band. Resolve it the way the build will.
if command -v dotnet >/dev/null 2>&1; then
  resolved=$(cd "$REPO_ROOT" && dotnet --version)
  printf '  %-12s %s (resolved in this directory)\n' 'sdk' "$resolved"
  case "$resolved" in
    10.*) ;;
    *)
      printf '  %-12s expected the 10.x band; global.json is not being honoured\n' 'sdk'
      missing=$((missing + 1))
      ;;
  esac
fi

if ((missing > 0)); then
  ((CHECK_ONLY)) && fail "$missing requirement(s) unmet"
  log 'installing the declared toolchain'
  command -v mise >/dev/null 2>&1 || fail 'install mise first, then re-run'
  run mise install
  run dotnet tool restore
  run pre-commit install --install-hooks
  run pre-commit install --hook-type pre-push
  run pre-commit install --hook-type pre-merge-commit
else
  log 'toolchain satisfied'
fi
