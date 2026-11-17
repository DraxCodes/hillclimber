# Workflow base (vendored)

This directory is a **pinned, vendored copy** of the `drax-ai-workflow` base at
**v0.1.0**. It is committed, reviewable and hash-checked. Do not edit the generated
files here.

## What is generated

| Path | Owner |
| --- | --- |
| `.opencode/agents/` | Base |
| `.opencode/commands/` | Base (the engineering pack adds `setup-engineering-pack`) |
| `.opencode/skills/` | Base |
| `.opencode/tools/` | Base (the `base` CLI) |
| `.opencode/packs/` | Base |
| `.opencode/.workflow-manifest.json` | Generated; records per-file hashes |
| `.opencode/workflow.json` | **Project** — the seam |
| `AGENTS.md` outside the managed block | **Project** |
| `opencode.json` | **Project** — provider, model, permissions, compaction |

## The seam

`.opencode/workflow.json` is the project-edited contract: checks (`build`, `test`,
`free`, `paid`), the visual `verify` hints, the `docs` paths and the `packs`. Its
schema is in the `workflow-base` skill's `references/seam.md`.

## Daily commands

```sh
node .opencode/tools/base.mjs check .
node .opencode/tools/base.mjs doctor .
node .opencode/tools/base.mjs status .
```

## Update and eject

From a base checkout (`C:\Users\joelp\code\drax-ai-workflow`, or `--from <dir>`):

```sh
node .opencode/tools/base.mjs update .
node .opencode/tools/base.mjs eject .
```

`update` refuses locally modified files unless `--force`. `eject` removes the
base-owned clean files and the `AGENTS.md` block and leaves the seam in place.

Run the floor after an update and commit the vendored tree, the manifest, the seam
version and the `AGENTS.md` block together.

## Engineering pack

The optional engineering pack is vendored under `.opencode/packs/engineering/` and
selected by the seam's `packs` array. Install its third-party skills with:

```sh
node .opencode/tools/base.mjs pack install engineering .
```

They land in `.agents/skills/` (gitignored) and never touch `~/.config`.

## Where the project's own tooling lives

The app, its tests, the UI capture scripts (`scripts/ui`), the playback fixture and
the design docs (`docs/ui-contract.md`, `docs/ui-art-direction.md`, the rubric
addendum) belong to the repo, not the base. See the root `README.md`.