# Rinzler78.Toolkit

<!-- shared:start -->

## What this repository is

The toolchain foundation of the `Here.Sdk.*` ecosystem. It publishes two packages
and holds no ecosystem code:

| Package | What it is |
|---|---|
| `Rinzler78.Build` | A props/targets package, auto-imported on restore, carrying every build semantic: target framework sets, analysers, warnings as errors, deterministic build, SourceLink, symbols, XML documentation, trimming and coverage contracts, packaging metadata. |
| `Rinzler78.Templates` | A `dotnet new` template pack — `rinzler-lib`, `rinzler-binding`, `rinzler-app` — each expanding a complete, self-contained agent harness. |

It sits in **no layer** of the eight-layer architecture: it is a toolchain artefact,
not an ecosystem package.

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

## Commands

Every repository in the ecosystem exposes the same verbs at the same place:

    scripts/setup-env.sh [--check]   scripts/build.sh     scripts/test.sh
    scripts/run.sh                   scripts/package.sh   scripts/lint.sh
    scripts/format.sh                scripts/coverage.sh  scripts/e2e.sh
    scripts/clean.sh                 scripts/publish.sh

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
