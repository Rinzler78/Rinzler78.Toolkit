# Vendored skills

The seventeen skills of the ecosystem are committed here and, byte for byte, in
`.claude/skills/`. Physical duplication rather than symbolic links: without
Developer Mode, a Windows checkout degrades a symlink into a text file holding the
target path.

They are **not yet vendored**. Content must come from the public upstream, and be
fetched by a repository-local command that commits the result, so that an update is
a reviewable pull request rather than a silent mutation.

Copying from a local machine store is not an acceptable substitute: those copies
carry personal customisations — one of them is written in French — and vendoring
them would propagate a private fork of a public skill into eighteen repositories.
