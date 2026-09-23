# Rinzler78.Toolkit

<!-- shared:start -->

## What this repository is

A domain-agnostic toolchain foundation. It publishes two packages and belongs to no
ecosystem:

| Package | What it is |
|---|---|
| `Rinzler78.Build` | A props/targets package, auto-imported on restore, carrying every build semantic: analysers, warnings as errors, deterministic build, SourceLink, symbols, XML documentation, and declarative contracts for trimming, coverage and architecture. |
| `Rinzler78.Templates` | A `dotnet new` template pack — `rinzler-lib`, `rinzler-binding`, `rinzler-app` — each expanding a complete, self-contained agent harness. |

It belongs to no domain. `Here.Sdk` is its first consumer, not its subject.

## Invariants

- **This repository has no assembly with a public surface.** Coverage floors,
  mutation scores, trimming contracts, API documentation, API-coverage tests and the
  public API baseline do not apply. Everything else does — analysers, warnings as
  errors, English, pre-commit, linear history, specification validation.
- **Its verification is regeneration.** The harness in this repository is generated
  from its own template and compared byte for byte. A divergence fails the build.
- **Templates are generated, never synchronised.** A repository created from a
  template owns its copy. There is no live link, and none will be added.
- **The harness is self-contained.** Skills, hooks, agents and commands are
  committed. Nothing is fetched at run time.
- **Specifications name libraries, not versions.** Concrete versions live in
  `Directory.Packages.props`.
- **Mechanism here, policy there.** This package knows how to refuse, not what to
  refuse. Layer vocabulary, forbidden dependencies, required analysers and any
  mandatory description fragment are declared by the consuming repository. A
  project that declares none of them builds: a toolkit that failed a project for
  having no architecture would be unusable outside the one ecosystem it was
  written for.

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
