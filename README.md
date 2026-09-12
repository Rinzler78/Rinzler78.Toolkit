# Rinzler78.Toolkit

The toolchain foundation of the `Here.Sdk.*` ecosystem: build semantics that apply
identically in an IDE and in continuous integration, and project templates that
expand a complete, self-contained agent harness.

| Package | What it is |
|---|---|
| `Rinzler78.Build` | A props/targets package, auto-imported on restore. Target framework sets, analysers, warnings as errors, deterministic build, SourceLink, symbol packages, XML documentation, trimming and coverage contracts, packaging metadata. |
| `Rinzler78.Templates` | `dotnet new` templates — `rinzler-lib`, `rinzler-binding`, `rinzler-app` — each expanding the harness, the pre-commit configuration, the workflows and the script facade. |

## Getting started

    ./scripts/setup-env.sh --check   # reports what is missing, changes nothing
    ./scripts/setup-env.sh           # converges the machine to the declared toolchain
    ./scripts/build.sh
    ./scripts/test.sh

Every repository in the ecosystem exposes the same verbs at the same place:
`setup-env`, `build`, `test`, `run`, `package`, `lint`, `format`, `coverage`,
`e2e`, `clean`, `publish`.

## Design notes

Templates are **generated, never synchronised**. A repository created from a
template owns its copy; there is no live link back to this one. Updating a harness
is a reviewable pull request in the repository that owns it.

This repository ships no assembly with a public surface, so the gates that
presuppose one — coverage floors, mutation scores, trimming contracts, API
documentation and the public API baseline — do not apply to it. Its own
verification is that it regenerates its harness from its own template, byte for
byte.

## Licence

MIT. Not affiliated with, endorsed by, or supported by HERE Technologies.
