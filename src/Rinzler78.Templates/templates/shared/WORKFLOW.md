# Workflow

How work moves from an idea to a published package, in every repository of the
ecosystem. This file belongs to the harness: the template delivers it, and it is the
same in every repository. An agent opened on this repository alone follows it without
asking, and stops only where [Stop and ask](#stop-and-ask) says so.

## Branches

- `develop` is where work lands. `master` receives only promotions from `develop`, and
  pre-releases and releases are cut from it.
- Every change to `develop` or `master` goes through a pull request — promotions and
  submodule bumps included. The one exception is creating `master` once, when a
  repository is provisioned.
- Work happens on `feature/<kebab-slug>`, cut from `develop`, in its own worktree at
  `.worktrees/<kebab-slug>/`. A branch is never reused once its pull request merges;
  its worktree is removed with it.

## Working

- Test first: the failing test, then the change that makes it pass. A fix carries the
  regression test that fails without it.
- Root cause first. No bypass, skip, ignore entry, suppression or commented test —
  unless blocked entirely, the diagnosis stated, and the decision explicitly approved.
- The three git gates (`pre-commit`, `pre-push`, `pre-merge-commit`) have no opt-out.
  A block names something to fix.
- Commits are signed; the rulesets refuse unsigned ones. Messages, code and
  documentation are in English, and never mention an AI agent or carry an attribution
  trailer.
- A comment, a README or a document that is no longer true is a defect, like code.

## Delivering a pull request

A pull request is not delivered when it is opened. Loop until it is green:

1. **Review it locally**: the diff against the merge base — code, tests, comments,
   documentation and READMEs alike — and `./scripts/checks.sh --stage all`.
2. **Read Copilot's review**, which the ruleset requests on every push, and every
   comment on the pull request.
3. **Close each finding**: fix it at the root, or refute it in the pull request with
   evidence. A refutation stays open to the next review; if it is contested, keep
   looping.
4. **Push, and review again** — locally and through Copilot. There is no cap on rounds.

The pull request is green when no finding is open, locally or from Copilot, the
branch has no merge conflict, and the required checks pass. Then, without asking:
merge (squash into `develop`), pull the base branch, delete the branch and remove its
worktree. In a repository that is a submodule of `Here.Sdk.Meta`, advance its pin
there, through a pull request that follows the same loop.

## Releasing

A release starts only when it is asked for, or when it is proposed and approved. Once
it is, carry it to the end without asking again:

1. **Propose the version, with its evidence.** Where a package has a public API, the
   diff of `PublicAPI.Unshipped.txt` decides: a removed or reshaped member is breaking,
   an added one a minor change, an empty diff a patch. Elsewhere, the Conventional
   Commits since the last tag decide: `feat` a minor change, `fix` a patch, `!`
   breaking. Before 1.0.0, a breaking change raises the minor; 1.0.0 is a decision,
   never a consequence. Pre-releases are `-alpha.N`, `-beta.N` or `-rc.N`.
2. **Promote**: a pull request from `develop` to `master`, looped to green like any
   other, merged with a **merge commit** — never squashed, never rebased.
3. **Tag the merge commit**, signed and annotated, and push the tag:

       git fetch origin
       git tag --sign --annotate v0.2.0 --message v0.2.0 origin/master
       git push origin v0.2.0

   The push publishes, directly and irreversibly. `release.yml` refuses a tag outside
   `vMAJOR.MINOR.PATCH[-(alpha|beta|rc).N]`, a lightweight or unverified tag, and a
   tag on a commit `master` does not contain; `publish.sh` refuses a package whose
   manifest does not declare the tag's version. A mistaken tag is corrected with a new
   number: tags cannot be moved or deleted.
4. **Follow it to the end**: the release run, the GitHub release, and the version
   listed by `https://api.nuget.org/v3-flatcontainer/<id>/index.json` — nuget.org
   indexes minutes after it accepts a push.

## Creating a repository

Generate it from `Rinzler78.Templates`, public. Configure commit signing in the clone
before the first commit, create `master` from `develop`, and run
`bash scripts/_provision-forge.sh <owner>/<repository>`. The complete procedure,
including the nuget.org trusted publishing policy, is the runbook in `Here.Sdk.Meta`:
`docs/runbooks/nuget-publication.md`.

## Stop and ask

Everything above is done without asking, except:

- **pushing a release tag** the release was not approved for;
- **anything on the nuget.org account** — trusted publishing policies, the prefix
  reservation — which needs its owner signed in;
- **a decision that contradicts a written rule** — a specification, an ADR, this
  file: state the rule and the conflict, do not choose silently;
- **a lazy mechanism** — a bypass, a suppression, a disabled check — which needs an
  explicit approval after the diagnosis.
