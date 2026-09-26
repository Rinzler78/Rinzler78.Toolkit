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

## Releasing

`develop` is where work lands, through squashed pull requests. `master` only receives
promotions: a pull request from `develop`, merged with a merge commit, whose checks are
the costly ones — nothing expensive is discovered at release time.

A release is a tag. When we are certain, we push a signed, annotated tag on a commit of
`master`, and `release.yml` does the rest:

    git tag --sign --annotate v1.2.0 --message v1.2.0    # or v1.2.0-rc.1
    git push origin v1.2.0

`scripts/_release-tag.sh` refuses, before anything is built, a tag that is not
`vMAJOR.MINOR.PATCH` with an optional `-alpha.N`, `-beta.N` or `-rc.N`, a lightweight
tag, a tag GitHub does not verify as signed, and a tag on a commit `master` does not
contain. The version is the tag's: MinVer reads it, so any other build is the next patch
as an alpha — `1.2.1-alpha.0.3`, three commits after `v1.2.0` — and cannot be mistaken
for a release. Release notes are generated by GitHub from the pull requests.

Tags `v*` can be neither moved nor deleted. A mistaken tag is corrected with a new
number, never by rewriting the old one: nuget.org never replaces a version, and a moved
tag would name code other than the package it published.

## Publishing

Publication is **keyless**. The release workflow asks GitHub for a short-lived OIDC
token, nuget.org validates it against a trusted publishing policy naming this
repository and `release.yml`, and returns an API key valid for one hour. No long-lived
credential exists — not here, not in any sibling repository. A key copied into nineteen
repositories is nineteen copies of one credential to rotate, and one of them will be
forgotten.

The policy is registered once per repository, on nuget.org under *Trusted Publishing*:
repository owner `Rinzler78`, this repository, workflow file `release.yml`, environment
`release`, permitted to push new packages as well as new versions — a scope limited to
selected *existing* packages cannot push a new identifier, which every package is at its
first release. Its scope names **only the identifiers this repository publishes**,
never a whole namespace: a policy is a grant to the workflow that matches it, and a
namespace-wide scope would let a compromise of one repository replace the packages of
all the others.

The environment is not optional. A policy matches the workflow's *file name*, never
its ref, so without one any ref carrying a `release.yml` could mint a key. The `release`
environment admits tags `v*` only.

**None of that is generated with the repository.** Settings live in the forge, not in
the tree, and — the trap — GitHub silently *creates* an environment the first time a job
names it, with no protection at all. Before the first pull request:

    bash scripts/_provision-forge.sh <owner>/<repository>

It is idempotent, and applies the read-only workflow token, the `release` environment
restricted to tags `v*`, the rulesets of `develop` (squash, linear, signed) and
`master` (merge commits, signed), both requiring a pull request and the `verify` and
`lint` checks, and the ruleset that makes tags `v*` immutable. Commits must be signed:
configure signing in the clone before the first commit.

## Layout

    src/Rinzler78.Sample/          the code this repository publishes
    tests/Rinzler78.Sample.Tests/  its tests, on net10.0, xUnit v3
    scripts/                       the verbs above
    scripts/hooks/                 this repository's own checks

## Licence

MIT. See `LICENSE`.
