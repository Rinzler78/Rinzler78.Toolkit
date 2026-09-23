# Templates

Three `dotnet new` templates, each expanding a complete, self-contained repository:

| Template | For |
|---|---|
| `rinzler-lib` | a library that publishes a package, on the portable framework set |
| `rinzler-binding` | a projection of a native library, with its upstream baselines and its recorded public surface |
| `rinzler-app` | an application, whose types are internal and whose tests see them by declaration |

## One copy of the harness

The harness lives once, in `shared/`, and each template sources it:

    templates/
      shared/            the harness — owned by the template, compared byte for byte
      seed/              the documents and manifests a repository owns after generation
      rinzler-lib/       what makes a library repository a library repository
      rinzler-binding/
      rinzler-app/

`sources` in each `template.json` reaches `../shared/` and `../seed/`, which the
template engine accepts as long as they sit inside the *installed package* — they do,
because the pack is installed as a whole. Three physical copies of forty harness
files would have drifted within a week; this way there is nothing to keep in sync.

## Harness and seed

`.template.config/seeds.txt` names the files a repository owns after generation: its
README, its entry points, its solution, its central versions, its dictionary and its
code. Everything else is harness, and `scripts/hooks/harness-regenerates.sh` compares
it byte for byte forever. The check reads that file rather than restating the list.

## What the engine cannot carry

`dotnet new` does not preserve the executable bit, so a generated repository's script
facade arrives as `0644` and its first commit would record that for every clone. The
`chmod` post action is not implemented by the .NET CLI host — it reports "the post
action is not supported" and exits non-zero. So the mode is a checked property
instead: `scripts/hooks/scripts-are-executable.sh` refuses a commit that records a
non-executable script, and `./scripts/setup-env.sh` restores it.

For the same reason the pre-commit configuration invokes its entry as
`bash scripts/checks.sh`: the check that reports the mode has to be able to run.
