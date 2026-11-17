# Engineering pack

An optional pack of third-party engineering skills. It is **not** part of the
universal base; a project selects it by adding `"engineering"` to the seam's
`packs` array and installing the base.

## What it adds

- `mattpocock/skills`: `setup-matt-pocock-skills`, `code-review`,
  `codebase-design`, `improve-codebase-architecture`, `setup-pre-commit`
- `addyosmani/agent-skills`: `documentation-and-adrs`
- `anthropics/skills`: `webapp-testing`

Plus the `setup-engineering-pack` command, which documents the setup step.

## Install

```sh
node .opencode/tools/base.mjs pack install engineering .
```

The installer reads `pack.json` beside itself and runs the `skills` CLI in project
scope, so the skills land in `.agents/skills/`, which OpenCode reads. Whether they
are committed is the project's choice; a project's `.gitignore` may ignore them.
It never writes `~/.config`.

The pack's own files (this README, `pack.json`, `install.mjs`) are vendored and
hash-checked like the rest of the base; the third-party skills are not. Re-run the
installer after an update to pick up a changed source list.

## Why it is separate

The universal base must stay project-agnostic and dependency-free. Third-party
skills change hands, add install-time network access, and are opinionated about
process. Keeping them in a pack means a project that does not want them never
carries them, and a project that does can pin the pack independently.