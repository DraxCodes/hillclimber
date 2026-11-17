# Spec: workflow-base — one authoritative source for the project-agnostic workflows

**Status:** accepted (council decision 2026-11-17)
**Council:** [council.md](council.md) — options, dissent, trade-off register
**Fixed point for review:** `1d84821` (session start)
**Feature slug:** `workflow-base`

---

## 1. Context

The workflow content in this repo (council, hillclimb + agents + rubric, grilling, handoff,
issue-tracker and domain-doc conventions, security lens, session hygiene, conventions) is
project-agnostic, but it is distributed by hand: `.opencode/README.md` still says "copy the
`.opencode/` directory, `opencode.json`, and `AGENTS.md` into a new project". The result is
the stale, partial global copy at `%USERPROFILE%\.config\opencode` (four agents 140 lines
behind, no global `AGENTS.md`/commands/skills, a partial `opencode.json`) and two copies
that can silently merge.

The council chose **committed vendoring from a dedicated base repo** with a validated seam.
This spec implements that choice.

## 2. Goal

Make `opencode-workflows` the only authored source of the universal workflows; make every
participating project carry a pinned, reviewable, hash-checked copy plus a small seam; keep
every project-specific command, tool, doc and flow inside the project that owns it; make a
brand-new project setup one documented command; and migrate this repo without behavioural
regression.

## 3. Ownership split

### The base owns (authored once, vendored everywhere)

- `agents/`: `task-orchestrator`, `plan-architect`, `quality-gatekeeper`, `security-auditor`.
- `commands/`: `council`, `hillclimb`, `grill-me`, `handoff`.
- `skills/`: `council`, `hillclimb`, `grilling`, `handoff`, `workflow-base`, `find-skills`,
  `context-engineering`; the 1–10 rubric as `skills/hillclimb/references/hillclimb-rubric.md`.
- `skills/workflow-base/references/`: `issue-tracker.md`, `domain.md`, `seam.md`.
- `packs/engineering/`: the generic optional pack (manifest, installer, README,
  `setup-engineering-pack` command), selected per project.
- `tools/base.mjs` + `tools/lib/`: the CLI, vendored into each project.
- `schema/workflow.schema.json`, `templates/`, `AGENTS.base.md`, `README.md`.

### The project owns (never authored or overwritten by the base)

- `.opencode/workflow.json` — the seam (project-edited; the base validates it).
- `AGENTS.md` outside the managed block — project instructions, standing preferences,
  dev-loop specifics.
- `opencode.json` — provider, model, permissions, compaction, tool output.
- The app, tests, fixtures, dev/status/UI scripts, project docs (`ui-contract`,
  `ui-art-direction`, the rubric addendum), `GLOSSARY.md`, `docs/adr/`, `.scratch/`.
- Any bespoke agent/command/skill added with a distinct ID.

### Authority rules

1. **The base never writes `opencode.json`.** `base print-config` emits a fragment; the
   human merges it. `base doctor` checks the effective values and warns.
2. **Base agents carry no `model:` and no `permissions`.** They inherit the project model;
   project permissions only append. The project config supplies the cheap per-agent models
   and the read-only deny rules for the three subagents.
3. **Project beats base at the same ID** only for project-owned additions; the vendored
   files are base-owned and are not edited in place. A project that needs a different
   behaviour adds a distinct ID or changes the seam, not the generated file.
4. **Universal docs are addressed skill-relatively** (`the hillclimb skill's
   references/hillclimb-rubric.md`), never by a global absolute path.

## 4. Base repo layout

```
C:\Users\joelp\code\opencode-workflows\
  package.json                 # type: module; bin: base -> tools/base.mjs; test: node --test
  VERSION                      # 0.1.0
  README.md                    # install, init, update, check, doctor, eject, seam, precedence
  AGENTS.base.md               # the managed block injected into a project AGENTS.md
  agents/
    task-orchestrator.md
    plan-architect.md
    quality-gatekeeper.md
    security-auditor.md
  commands/
    council.md
    hillclimb.md
    grill-me.md
    handoff.md
  skills/
    council/SKILL.md
    hillclimb/SKILL.md
    hillclimb/references/hillclimb-rubric.md
    grilling/SKILL.md
    handoff/SKILL.md
    workflow-base/SKILL.md
    workflow-base/references/issue-tracker.md
    workflow-base/references/domain.md
    workflow-base/references/seam.md
    find-skills/SKILL.md
    context-engineering/SKILL.md
  packs/
    engineering/
      pack.json
      install.mjs
      README.md
      commands/setup-engineering-pack.md
  schema/workflow.schema.json
  templates/workflow.example.json
  tools/
    base.mjs
    lib/cli.mjs
    lib/seam.mjs
    lib/manifest.mjs
    lib/plan.mjs
    lib/apply.mjs
    lib/doctor.mjs
    lib/hash.mjs
  tests/*.test.mjs
  docs/adr/0001-vendored-base.md
```

`VERSION` is the release version. Git tags (`v0.1.0`) are the immutable store; a project
pins by checking out a tag in the base repo and running `base update`.

## 5. The seam

### 5.1 `.opencode/workflow.json` (schema version 1)

```json
{
  "schemaVersion": 1,
  "base": { "name": "opencode-workflows", "version": "0.1.0" },
  "project": "HillClimberUI",
  "workflows": ["council", "hillclimb", "grilling", "handoff", "security"],
  "checks": {
    "build": { "command": "dotnet build --nologo", "cwd": "." },
    "test": { "command": "dotnet test --nologo", "cwd": "." },
    "free": [
      { "command": "node theme-nav.mjs", "cwd": "scripts/ui" },
      { "command": "node security.mjs", "cwd": "scripts/ui" },
      { "command": "node providers.mjs", "cwd": "scripts/ui" }
    ],
    "paid": [{ "command": "node surfaces.mjs", "cwd": "scripts/ui" }]
  },
  "verify": {
    "surface": "web",
    "seed": "CLIMBER_SEED",
    "capture": "node polish.mjs",
    "captureCwd": "scripts/ui",
    "widths": [375, 900],
    "themes": ["light", "dark"],
    "notes": "First-run and keyboard read-through; stop the dev host before dotnet test on Windows; token-spending surfaces.mjs only with explicit approval."
  },
  "docs": {
    "issues": ".scratch",
    "glossary": "GLOSSARY.md",
    "adr": "docs/adr",
    "rubricAddendum": "docs/agents/hillclimb-addendum.md",
    "contract": "docs/ui-contract.md",
    "artDirection": "docs/ui-art-direction.md"
  },
  "neverCommit": [".env", "*.pem", "*.key", "*.local.json", "appsettings.*.local.json"],
  "packs": ["engineering"]
}
```

Rules:

- Required: `schemaVersion`, `base.name`, `base.version`, `project`, `checks`.
- `checks.build` / `checks.test` are `{ command, cwd? }` or `null` for a project with no
  such step. `checks.free` / `checks.paid` are arrays of the same shape.
- `verify.surface` is `web | cli | api | docs | none`; `none` selects the measurable-metric
  mode. For `web`, `widths` and `themes` are required.
- `docs` paths are project-relative; a listed path that does not exist is a `doctor`
  **warning** for optional docs and an **error** for `issues` and `adr`.
- Unknown keys are rejected. `base.version` must equal the manifest version.
- Commands are never executed by `base`; shell blocks in command templates remain the
  project's responsibility.

### 5.2 The managed `AGENTS.md` block

`base init` inserts, and `base update` replaces, a block delimited by:

```md
<!-- workflow-base:begin -->
...
<!-- workflow-base:end -->
```

Content (from `AGENTS.base.md`, ~45 lines):

- "This project uses the vendored workflow base vX.Y.Z. The seam is
  `.opencode/workflow.json`; if it is missing, these workflows are dormant."
- Hillclimb trigger and gate, with the floor read from `checks`.
- Council trigger and divergence/convergence definition.
- The four working preferences as universal principles, with the project section supplying
  the specifics.
- Session hygiene and conventions.
- Pointers to the `workflow-base` skill (seam, conventions, update/eject) and the
  `hillclimb` skill (rubric).

The project's own text lives outside the markers and is never touched.

### 5.3 The manifest

`.opencode/.workflow-manifest.json` is committed:

```json
{
  "schemaVersion": 1,
  "base": "opencode-workflows",
  "version": "0.1.0",
  "commit": "<base HEAD sha>",
  "installedAt": "2026-11-17T00:00:00.000Z",
  "files": { ".opencode/agents/task-orchestrator.md": "sha256:…" },
  "agentsBlockHash": "sha256:…",
  "packs": ["engineering"]
}
```

Hashes are computed over content normalized to LF, so a CRLF checkout does not report drift.

## 6. CLI contract (`base`)

All commands accept `[target]` (default `.`), are non-interactive when flags supply the
values, and exit `0` on success, `1` on a failed check/validation, `2` on usage error.

| Command | Behaviour |
| --- | --- |
| `base init [dir]` | Requires a source checkout. Copies the manifest-owned tree, writes the seam skeleton (flags fill it), injects the AGENTS block, writes the manifest. Refuses an existing manifest unless `--force`. Prints a diff stat and next steps. |
| `base update [dir]` | Reads the manifest, verifies local hashes, copies the current base version (or `--from <dir>`), writes atomically (stage → validate → replace), updates the manifest + block. Refuses locally modified files unless `--force`. |
| `base check [dir]` | Drift + version + seam validation. Prints each changed/missing/extra file. Exit `1` on any drift. |
| `base doctor [dir]` | Schema validation, required doc paths, model/permissions warnings, same-ID global agent shadowing, AGENTS block hash, secret scan of the vendored tree, pack state. Exit `1` on errors. |
| `base status [dir]` | Version, commit, per-file state (clean/modified/missing), seam summary, pack state. |
| `base eject [dir]` | Removes manifest-owned clean files, the AGENTS block and the manifest; reports unowned files. Refuses modified files unless `--force`. |
| `base print-config [dir]` | Prints the recommended `opencode.json` fragment: root model placeholder, per-agent model placeholders, read-only deny rules for the three subagents. Never writes. |
| `base pack install <name> [dir]` | Runs `packs/<name>/install.mjs` with cwd = target. |
| `base version` | Prints `VERSION`. |

Flags for `init`: `--project`, `--test`, `--build`, `--free` (repeatable), `--paid`
(repeatable), `--surface`, `--seed`, `--capture`, `--capture-cwd`, `--issues`, `--glossary`,
`--adr`, `--rubric-addendum`, `--pack` (repeatable), `--force`, `--dry-run`.

### Safety and Windows behaviour

- No network. The source is the local checkout (`--from`, or `WORKFLOW_BASE`, or the
  detected checkout).
- All writes go through a plan (`plan.mjs`) and an applier (`apply.mjs`): validate every
  destination first, back up replaced files under `.opencode/.workflow-backup/<ts>/`,
  write, and restore on error. No half-written tree.
- Copy, never symlink (no developer-mode dependency). LF content; git handles checkout
  line endings.
- Never write `opencode.json`, never print secret values, never follow a seam command.

## 7. This repo's migration

### Add

| Path | Content |
| --- | --- |
| `.opencode/workflow.json` | The seam in §5.1. |
| `.opencode/.workflow-manifest.json` | Generated by `base init`. |
| `.opencode/tools/base.mjs` + `.opencode/tools/lib/*` | Vendored CLI. |
| `.opencode/agents/*`, `.opencode/commands/{council,hillclimb,grill-me,handoff}.md`, `.opencode/skills/*` | Vendored universal content (replaces the current copies). |
| `.opencode/packs/engineering/*`, `.opencode/commands/setup-engineering-pack.md` | Vendored optional pack. |
| `docs/agents/hillclimb-addendum.md` | Probes 8–13, the provenance/art-direction caps, and the UI harness floor details moved out of the universal rubric. |
| `docs/adr/0003-workflow-base.md` | The ADR for the extraction. |

### Change

| Path | Change |
| --- | --- |
| `AGENTS.md` | Replace the universal workflow sections with the managed block; keep project-specific dev loop, local models, app notes, and the Surplus council-seat table; point the issue-tracker/domain references at the `workflow-base` skill. |
| `opencode.json` | Add `agents.*.model` for the four agents (preserve today's cheap models) and `agents.{plan-architect,quality-gatekeeper,security-auditor}.permissions` edit-deny; keep provider/permissions/compaction. |
| `.opencode/README.md` | Replace the "copy the directory" story with: this is a vendored workflow base, pinned at v0.1.0; update/eject via `base`; project tooling is in the repo README. |
| `README.md` | Add a short "Agent workflows" section pointing at `.opencode/workflow.json` and the base repo; no workflow content duplicated. |
| `.gitignore` | Keep `.agents/skills/` + `skills-lock.json` ignored; add `.opencode/.workflow-backup/`. |

### Delete (content moved to the base)

| Path | Why |
| --- | --- |
| `docs/agents/issue-tracker.md` | Now `workflow-base` skill `references/issue-tracker.md`. |
| `docs/agents/domain.md` | Now `workflow-base` skill `references/domain.md`. |
| `docs/agents/hillclimb-rubric.md` | Universal rubric moves to the skill; the project keeps only the addendum. |
| `.opencode/engineering-pack.json`, `.opencode/scripts/install-engineering-pack.mjs` | Moved to `packs/engineering` in the base and vendored under `.opencode/packs/engineering/`. |

### Machine migration (not committed)

- Back up `%USERPROFILE%\.config\opencode\agents` to
  `%USERPROFILE%\.config\opencode\agents.bak-2026-11-17`, then remove the four `.md` files
  from the discovered `agents/` directory.
- Leave the global `opencode.json` as machine config; it is not base-owned. Record the
  missing model/compaction/permissions as a separate optional cleanup, not part of this
  change.
- `base doctor` must fail while a same-ID global agent exists.

## 8. Verification

### Base repo

- `node --test` in `opencode-workflows`:
  - schema: valid example accepted; unknown key, missing required, bad type rejected;
  - `init` into a temp dir writes the expected tree, seam, block and manifest;
  - `check` detects modified, missing and extra files;
  - `update` is atomic: a forced write failure leaves the old tree intact;
  - `doctor` detects a same-ID global agent (temp `USERPROFILE`/`HOME`), an empty
    `checks.test`, a missing required doc path and a stale block hash;
  - `eject` removes only manifest-owned clean files;
  - hash normalization ignores CRLF/LF differences;
  - the base tree contains no project names/commands (`dotnet`, `scripts/ui`,
    `CLIMBER_SEED`, `/status`, `Razor`, `surplus/`) outside the example/seam docs.

### This repo

- `dotnet test --nologo` — real output, count compared with the 764-at-merge baseline.
- `node .opencode/tools/base.mjs check .` → clean.
- `node .opencode/tools/base.mjs doctor .` → no errors (warnings listed).
- Fresh-repo walkthrough: `base init` into a temp directory, fill a minimal seam, run
  `check`/`doctor`, inspect what OpenCode would discover. This is the evidence that the
  load-path/opt-in claim holds.
- `git status` clean before each commit; atomic commits per lane.

### Not run (state plainly)

- The token-spending `scripts/ui/surfaces.mjs` harness (no app change; not needed).
- The Playwright harnesses (no visual change; the app is not modified).
- No `dotnet test` result is claimed until it is run in this session.

## 9. Out of scope

- Adding new workflow content or changing any workflow's intent.
- Migrating other projects (the base is ready for them; this spec migrates this repo only).
- A GitHub remote for the base (the user chose "later").
- Changing the app, its tests, or the dev loop.
- Fixing the global `opencode.json` (model/compaction/permissions are machine config).
- A global bootstrap or convenience tier (option C); it can be added later on top of A.
- Rewriting the engineering pack's third-party skills.

## 10. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| The vendored copy drifts from the base | `base check` in the project; manifest hashes; generated header; base tests. |
| A local edit is silently overwritten | `update` refuses modified files unless `--force`; backup before replace. |
| The stale global agents merge with the vendored ones | Back up and remove them; `doctor` fails on same-ID global agents. |
| A malformed seam makes an agent guess | Schema validation; `doctor` errors on empty required checks; commands refuse with one line. |
| Secrets leak into the base or a project | `{env:}` only; `neverCommit` patterns; `doctor` scans the vendored tree; no secret values in output. |
| Windows write/line-ending issues | Plan/apply with backup; copy not symlink; LF content; CRLF-normalized hashes. |
| The base tool itself becomes a copy that drifts | It is vendored and hashed with the rest; `update` replaces it; the base tests are its floor. |
| This repo's floor regresses | `dotnet test` is the floor; the app is not touched; every commit is atomic and reviewable. |

## 11. Review

`/code-review 1d84821` after implementation. The review checks Standards (repo conventions)
and Spec (this document + the council decision).