# Rinzler78.Sample

<!-- shared:start -->

## What this repository is

One sentence, then the detail. Replace this section before the first pull request:
a README that describes a repository nobody has written yet is the one document a
reader is guaranteed to believe.

| Package | What it is |
|---|---|
| `Rinzler78.Sample` | Say what it publishes, and for whom. |

## Invariants

- **Root cause first.** A symptom silenced is a diagnosis skipped.
- **The harness is self-contained.** Skills, hooks, agents and commands are
  committed. Nothing is fetched at run time.
- **The harness is generated, never synchronised.** This repository owns its copy;
  there is no live link to the template it came from.
- **Specifications name libraries, not versions.** Concrete versions live in
  `Directory.Packages.props`.
- **This repository declares its layer** in `Directory.Build.props`, and the policy
  package validates it. A repository that declares no policy still builds.

## Commands

Every repository in the ecosystem exposes the same verbs at the same place:

    scripts/setup-env.sh [--check]   scripts/restore.sh   scripts/build.sh
    scripts/test.sh                  scripts/coverage.sh  scripts/watch.sh
    scripts/run.sh                   scripts/deploy.sh    scripts/package.sh
    scripts/publish.sh               scripts/docs.sh      scripts/lint.sh
    scripts/format.sh                scripts/e2e.sh       scripts/bench.sh
    scripts/clean.sh                 scripts/checks.sh [--stage pre-commit|pre-push]

The repository's own checks are one script each in `scripts/hooks/`, declaring
`# rinzler-stage:` in their header; `scripts/checks.sh` discovers them, and nothing
enumerates them a second time. Adding a check is adding a file.

Three gates carry different responsibilities: `pre-commit` runs the fast checks and
only the tests the staged change can reach, `pre-push` runs the whole unit suite and
the network scans, `pre-merge-commit` runs the integration checks and coverage.
`scripts/test.sh --staged`, `--changed` and `scripts/watch.sh --tests` use the same
computed selection. Tests are run by the test project itself, not by `dotnet test`.

Speed and configuration are parameters, not verbs: `CONFIGURATION=Release` selects
the configuration, and there is deliberately no `fast-build`. A variant script is a
second build semantics that continuous integration never exercises.

`--check` is non-mutating and is a continuous integration gate.

## Working agreement

- Root cause first. A symptom silenced is a diagnosis skipped.
- No lazy mechanism — no bypass, skip, ignore entry, suppression or commented test —
  without a stated diagnosis and an explicit decision recorded in an ADR.
- Blocking hooks have no opt-out. A block names something to fix.
- Test-driven development for every feature and every fix.
- One branch, one worktree under `.worktrees/<slug>`, never reused.
- English everywhere in the repository: code, comments, commits, documentation.

<!-- shared:end -->

## Codex and cross-agent specifics

This file and `CLAUDE.md` carry the same shared block. A check fails the build
if they diverge; edit both, or edit `CLAUDE.md` and regenerate.
