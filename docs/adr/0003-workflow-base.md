# 0003 — Adopt the vendored workflow base

**Status:** accepted (2026-11-17)
**Context:** the agent workflows in this repo (council, hillclimb, grilling,
handoff, security lens, 1–10 rubric) are project-agnostic, but they were copied by
hand into `.opencode/` and partly into `~/.config/opencode`, which drifted.

## Decision

This repo becomes a **consumer** of the `drax-ai-workflow` base, pinned at
v0.1.0.

- The base owns `.opencode/agents/`, `.opencode/commands/`, `.opencode/skills/`,
  `.opencode/tools/`, `.opencode/packs/` and the managed block in `AGENTS.md`.
- This repo owns `.opencode/workflow.json` (the seam), `.opencode/.workflow-manifest.json`
  as generated, `AGENTS.md` outside the block, `opencode.json`, the app, the tests,
  `scripts/ui`, the design docs and the rubric addendum.
- `node .opencode/tools/base.mjs check .` detects drift; `update` bumps; `doctor`
  validates.

The universal rubric moved into the `hillclimb` skill's
`references/hillclimb-rubric.md`. This repo keeps only the UI-specific probes and
caps in [`docs/agents/hillclimb-addendum.md`](../agents/hillclimb-addendum.md),
which the seam names as `docs.rubricAddendum`.

## Why

- The universal content has one authored source instead of two copies that merge
  silently.
- The copy is in this repo's diff, so an update is reviewed like code.
- This repo's provider, model, permissions, design docs and harnesses stay local.
- The base never writes `opencode.json`; it only emits a fragment to merge.

The distribution decision and its dissent are recorded in the base repo's
`docs/adr/0001-vendored-base.md` and in `.scratch/workflow-base/council.md` here.

## Consequences

- An update is a real diff: vendored files, the manifest, the seam version and the
  `AGENTS.md` block move together.
- A same-ID global agent shadows the vendored one; `doctor` fails on it, so the
  stale global agents were moved aside.
- The floor is the seam's `checks`: `dotnet build`, `dotnet test`, and the free
  `scripts/ui` assertions. The token-spending `surfaces.mjs` stays opt-in.
- Editing a generated file in place is drift; the fix belongs in the base repo or
  in a project file with a distinct ID.