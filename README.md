# Rinzler78.Toolkit

A domain-agnostic toolchain foundation: build semantics that apply identically in
an IDE and in continuous integration, and project templates that expand a complete,
self-contained agent harness.

It knows how to refuse, not what to refuse. Accepted layer names, forbidden
dependencies, required analysers and any mandatory description fragment are
**declared by the consuming repository**; a project that declares none of them
builds, rather than being failed for having no architecture. The `Here.Sdk`
ecosystem is its first consumer, not its subject.

| Package | What it is |
|---|---|
| `Rinzler78.Build` | A props/targets package, auto-imported on restore. Analysers, warnings as errors, deterministic build, SourceLink, symbol packages, XML documentation, and the declarative contracts below. |
| `Rinzler78.Templates` | `dotnet new` templates — `rinzler-lib`, `rinzler-binding`, `rinzler-app` — each expanding the harness, the pre-commit configuration, the workflows and the script facade. |

## Getting started

    ./scripts/setup-env.sh --check   # reports what is missing, changes nothing
    ./scripts/setup-env.sh           # converges the machine to the declared toolchain
    ./scripts/build.sh
    ./scripts/test.sh

Every repository in the ecosystem exposes the same verbs at the same place:

| Verb | What it does |
|---|---|
| `setup-env` | Converges the machine to the declared toolchain; `--check` reports without mutating. |
| `restore` | Locked in continuous integration, permissive locally. |
| `build` `test` `coverage` | `CONFIGURATION=Release` selects the configuration; there is no separate release verb. |
| `watch` | The fast inner loop: the same build, re-triggered on change. |
| `run` `deploy` | Run locally, or deploy to an emulator, simulator or device. |
| `package` `publish` | Pack, then push to nuget.org. |
| `docs` | Build the API documentation site. |
| `lint` `format` | The blocking checks, and the formatter that satisfies them. |
| `e2e` | The end-to-end suite. |
| `bench` | Measure the build, so that "fast" is a number. |
| `clean` | Remove everything not tracked. |

There is deliberately no `fast-build` and no `build-release`. Speed and configuration
are parameters, not actions: a variant script is a second build semantics that
continuous integration never exercises, which is how "it works on my machine" is
manufactured. `dotnet build` is already incremental.

### What the measurements said

`scripts/bench.sh` on this repository, three runs each:

| | |
|---|---|
| build, nothing changed | ~6.9 s |
| the same without restore | ~5.8 s |
| static graph restore enabled | 6.09–7.62 s |
| static graph restore disabled | 6.62–7.21 s |

Roughly a second of a no-op build is restore evaluation, and static graph evaluation
made no measurable difference — its ranges overlap. So nothing was optimised, and no
property was added on the strength of an intuition. The numbers are re-measurable by
running the verb.

## Design notes

Templates are **generated, never synchronised**. A repository created from a
template owns its copy; there is no live link back to this one. Updating a harness
is a reviewable pull request in the repository that owns it.

## What a consumer declares

| Property or item | Effect |
|---|---|
| `RinzlerKnownLayers` | The accepted layer vocabulary. Without it, layer checks are inert. |
| `RinzlerLayer` | The layer this project sits in, validated against the vocabulary. |
| `RinzlerForbiddenDependency` | Package identities this layer may not declare a dependency on. |
| `RinzlerRequiredAnalyzer` | Analysers this project must reference, each with a `Because`. |
| `RinzlerRequiredDescription` | A fragment every package description must carry. |
| `RinzlerTrimContract` | `OutOfScope`, `Trimmable` or `AotCompatible`. |
| `RinzlerCoverageContract` | `Enforced`, `Reduced` or `Exempt`. |

This repository ships no assembly with a public surface, so the gates that
presuppose one — coverage floors, mutation scores, trimming contracts, API
documentation and the public API baseline — do not apply to it. Its own
verification is that it regenerates its harness from its own template, byte for
byte.

## Licence

MIT.
