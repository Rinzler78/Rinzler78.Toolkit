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

    bash scripts/setup-env.sh        # the bootstrap: also restores the facade's mode
    ./scripts/setup-env.sh --check   # reports what is missing, changes nothing
    ./scripts/build.sh
    ./scripts/test.sh

The first call goes through `bash`: a repository expanded from the template arrives
without the executable bit — `dotnet new` cannot carry a file mode — so the facade
cannot restore its own mode by executing itself. Afterwards every call uses `./`, and a
commit that records a non-executable script is refused.

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
| `checks` | The repository's own checks, discovered in `scripts/hooks/`; `--stage` selects. |
| `e2e` | The end-to-end suite. |
| `bench` | Measure the build, so that "fast" is a number. |
| `clean` | Remove everything not tracked. |

There is deliberately no `fast-build` and no `build-release`. Speed and configuration
are parameters, not actions: a variant script is a second build semantics that
continuous integration never exercises, which is how "it works on my machine" is
manufactured. `dotnet build` is already incremental.

### On checks

A repository's own checks live in `scripts/hooks/`, one script each, and each one
declares in its header when it runs:

    # rinzler-stage: pre-commit    fast and offline
    # rinzler-stage: pre-push      slow, or needs the network

`scripts/checks.sh` discovers them. Nothing enumerates them a second time: the
inventory was previously written once in `.pre-commit-config.yaml` and once in the
workflow, and the two copies drifted within days — two checks ran in continuous
integration and nowhere else. Adding a check is adding a file. A check that declares
no stage is refused, not skipped, because a check nobody runs is worse than a check
nobody wrote.

### On gates

Three gates, three responsibilities, chosen by what each one costs and what it
protects:

| Gate | What runs | Why there |
|---|---|---|
| `pre-commit` | the fast offline checks, and **only the tests the staged change can reach** | a commit is part of the loop; it must cost seconds |
| `pre-push` | the whole unit suite, the vulnerability and freshness scans, the build contracts | the branch is leaving the machine |
| `pre-merge-commit` | the integration checks and the coverage figure | two histories are becoming one, and that is not a loop |

The selection is computed, not guessed: from the changed files, to the projects that
own them, to every project that references those transitively, down to the test
projects in that closure. It errs towards running too much — a change to a build
manifest, the toolchain pin or the facade itself selects everything — because a
selection that runs too little produces a green that means nothing.

    ./scripts/test.sh                 every test project
    ./scripts/test.sh --staged        only what the staged change reaches
    ./scripts/test.sh --changed       only what this branch changes
    ./scripts/watch.sh --tests        that same selection, re-run on every save

### On running tests at all

The tests are run by the test project itself — `dotnet run --project` — and not
through `dotnet test`. On the .NET 10 SDK that command offers two paths and neither
carries an xUnit v3 assembly of this version: the VSTest bridge is refused outright
by Microsoft.Testing.Platform, and the platform runner discovers zero tests. The
assembly is its own runner, and it ran them the moment it was asked directly. One
layer fewer, and the filters are the runner's own.

Coverage follows from that: a VSTest data collector has nothing to attach to, so
`dotnet-coverage` wraps the process instead.

### On publishing

Publication is **keyless**. The release workflow asks GitHub for a short-lived OIDC
token, nuget.org validates it against a trusted publishing policy naming this
repository and `release.yml`, and returns an API key valid for one hour. No long-lived
credential exists — not here, not in any sibling repository. A key copied into nineteen
repositories is nineteen copies of one credential to rotate, and one of them will be
forgotten.

The policy is registered once per repository, on nuget.org under *Trusted Publishing*:
repository owner `Rinzler78`, this repository, workflow file `release.yml`, environment
`release`, permitted to push new packages as well as new versions — a scope limited to selected *existing*
packages cannot push a new identifier, which is what every package here is.

The environment is not optional. A policy matches the workflow's *file name*, never
its branch, so without one any branch carrying a `release.yml` could mint a key — a
feature branch rewriting that file to trigger on its own push would publish without
review. The `release` environment admits deployments from `master` only, and the policy
requires it.

Its scope names **only the identifiers this repository publishes**, never the whole
`Rinzler78.*` namespace. A policy is a grant to the workflow that matches it, so a
namespace-wide scope would let a compromise of any one repository replace any package of
the ecosystem. Where a single glob cannot express a repository's identifiers — this one
publishes `Rinzler78.Build`, `Rinzler78.Templates` and `Rinzler78.Toolkit`, which share no
narrower prefix — that is one policy per identifier, not one wider glob.

The version is Release Please's, computed from Conventional Commits and written to
`version.txt`; `package.sh` reads it and nothing else does. `VERSION_SUFFIX` turns a
build into a prerelease, which is how a continuous integration build cannot be mistaken
for a release.

### On measuring

`scripts/bench.sh` times three points — a build with nothing changed, the same
without restore, and one with a project file touched — because they answer three
different questions, and optimising without knowing which one hurts is guessing.

It records the load average with the numbers and **refuses to measure a busy
machine**: a build time taken under unrelated load says more about the machine than
about the build, and a figure nobody can trust is worse than no figure, because it
gets quoted. `FORCE=1` records anyway, and the result should not be quoted.

No build property has been added on performance grounds. Static graph restore was
tried and removed: its ranges overlapped with the baseline, and a property that
changes restore semantics for no measured gain is cargo cult.

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
