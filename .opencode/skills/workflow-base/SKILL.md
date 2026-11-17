---
name: Workflow base
description: How this project's vendored workflow base works — the seam, ownership and precedence, daily check/update/eject, the issue tracker and domain-doc conventions
---

# Workflow base

This project carries a pinned, vendored copy of the **drax-ai-workflow** base
under `.opencode/`. The base provides the agents (`task-orchestrator`,
`plan-architect`, `quality-gatekeeper`, `security-auditor`), the commands
(`council`, `hillclimb`, `grill-me`, `handoff`), the skills (`council`,
`hillclimb`, `grilling`, `handoff`, `workflow-base`, `find-skills`,
`context-engineering`) and the `base` CLI.

Everything the base owns is generated. Do not edit it in a project — edit the base
repo and update. Everything else in the project is the project's own.

## The seam

`.opencode/workflow.json` is the project-edited seam: the only file the project
changes to describe its checks, verification, docs and packs. It is validated by
`base doctor` and its schema is documented in `references/seam.md`.

If the seam is missing, the workflows are dormant. Say so and stop rather than
guessing at a project's checks or docs.

## Ownership and precedence

| Owner | Paths |
| --- | --- |
| Base | `.opencode/agents/`, `.opencode/commands/`, `.opencode/skills/`, `.opencode/tools/`, `.opencode/packs/` |
| Project | `.opencode/workflow.json`, `.opencode/.workflow-manifest.json`, `AGENTS.md` outside the managed block, `opencode.json`, app, tests, docs, `.scratch/` |

- The base **never writes `opencode.json`**. The project owns provider, model,
  permissions, compaction and tool output. `base print-config` emits a fragment to
  merge by hand.
- Base agents carry no `model:` and no permissions. They inherit the project model;
  the project supplies per-agent models and the read-only edit-deny rules.
- A project that needs different behaviour adds a **distinct ID**, not an edit to a
  generated file. A project skill with the same ID as a vendored one overrides it,
  which is a drift the next update reports.
- Universal docs are addressed skill-relatively (for example "the `hillclimb`
  skill's `references/hillclimb-rubric.md`"), never by an absolute path.

## Daily commands

Run these from the project root; the CLI is vendored:

```sh
node .opencode/tools/base.mjs check .    # drift against the manifest
node .opencode/tools/base.mjs doctor .   # seam, docs, config, global shadowing, secrets
node .opencode/tools/base.mjs status .   # version, commit, per-file state
node .opencode/tools/base.mjs update .   # bump to the base checkout's version
node .opencode/tools/base.mjs eject .    # remove base-owned clean files and the AGENTS block
```

A brand-new project starts from a base checkout with `base new <dir> --preset web`;
see the base `README.md`. `base doctor --fix .` performs the mechanical fixes
(required doc dirs, same-ID global agents).

`update` refuses locally modified files unless `--force`. `eject` leaves the seam
in place for the project.

## Updating the base

1. Check out the target tag in a base checkout (pass `--from <dir>`, set
   `WORKFLOW_BASE`, or run from the checkout).
2. From the project: `node .opencode/tools/base.mjs update .`.
3. Review the diff, run the floor (the seam's `checks`), commit the vendored tree,
   the manifest, the seam's version and the `AGENTS.md` block together.
4. `node .opencode/tools/base.mjs check .` must be clean before the commit.

The manifest records per-file `sha256:` hashes (normalized to LF) and the AGENTS
block hash, so a CRLF checkout does not report drift.

## References

- `references/seam.md` — the seam schema, field by field.
- `references/issue-tracker.md` — where issues, specs and triage state live.
- `references/domain.md` — how to read the glossary and ADRs before exploring.

## Related skills

- `hillclimb` — the 1–10 rubric and the iterate/stop gate.
- `council` — divergence before a large feature.
- `grilling` — sharpen a fuzzy goal into settled decisions.
- `handoff` — write a session handoff that a fresh session can resume.