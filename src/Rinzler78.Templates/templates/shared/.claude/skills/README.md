# Vendored skills

The skills of the ecosystem are committed here and, byte for byte, in
`.claude/skills/`. Physical duplication rather than symbolic links: without
Developer Mode, a Windows checkout degrades a symlink into a text file holding the
target path.

Each skill comes from its public upstream, pinned to a full commit in
`scripts/skills.manifest`; `NOTICE.md` records the sources and their licences.
Nothing is fetched at run time. To update, run `scripts/sync-skills.sh` on a feature
branch: it refreshes both directories and commits the result, so an update is a
pull request someone can read. The check `scripts/hooks/skills-are-vendored.sh`
keeps the two directories identical and the set equal to the manifest.

A skill is added or removed by editing the manifest, never by editing a skill:
copies from a local machine store carry personal edits and are not a source.
