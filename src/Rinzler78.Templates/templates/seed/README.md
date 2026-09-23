# Rinzler78.Sample

One sentence saying what this repository is and who it is for. This file was written
by the template so that the repository has a true landing page from its first commit;
rewrite it for the subject, and keep it true — a blocking check reads it.

## Getting started

    bash scripts/setup-env.sh          # the first command, and the only one with `bash`
    ./scripts/restore.sh
    ./scripts/build.sh
    ./scripts/test.sh

The first invocation goes through `bash` on purpose: `dotnet new` cannot carry a file
mode, so on a fresh expansion the facade is not yet executable and cannot restore its
own mode by executing itself. `setup-env` restores it, and every later call uses `./`.

`--check` is non-mutating: it reports what is missing instead of installing it, which
is what makes it usable as a continuous integration gate.

## Commands

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

## On checks

A repository's own checks live in `scripts/hooks/`, one script each, and each one
declares in its header when it runs:

    # rinzler-stage: pre-commit    fast and offline
    # rinzler-stage: pre-push      slow, or needs the network

`scripts/checks.sh` discovers them; nothing enumerates them a second time. Adding a
check is adding a file, and a check that declares no stage is refused rather than
skipped.

## Gates

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

## Running tests

The tests are run by the test project itself — `dotnet run --project` — and not
through `dotnet test`. On the .NET 10 SDK that command offers two paths and neither
carries an xUnit v3 assembly of this version: the VSTest bridge is refused outright
by Microsoft.Testing.Platform, and the platform runner discovers zero tests. The
assembly is its own runner, and it ran them the moment it was asked directly. One
layer fewer, and the filters are the runner's own.

Coverage follows from that: a VSTest data collector has nothing to attach to, so
`dotnet-coverage` wraps the process instead.

## Layout

    src/Rinzler78.Sample/          the code this repository publishes
    tests/Rinzler78.Sample.Tests/  its tests, on net10.0, xUnit v3
    scripts/                       the verbs above
    scripts/hooks/                 this repository's own checks

## Licence

MIT. See `LICENSE`.
