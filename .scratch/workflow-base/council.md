# Council — workflow-base: one source for the project-agnostic workflows

**Date:** 2026-11-17
**Fixed point:** `1d84821` (`master`, clean; app stopped)
**Question:** What distribution mechanism and base↔project seam should make this repo's
project-agnostic agent workflows one authoritative source usable from any project,
replacing hand copies and the stale global install?

**Decision owner:** the user. This document records the debate; the `## Decision` section is
filled in only after the user chooses.

---

## 1. Context and constraints

### What is being extracted

Universal workflow content:

- the `/council` + `council` skill, `/hillclimb` + `hillclimb` skill + 1–10 rubric;
- the four agents (`task-orchestrator`, `plan-architect`, `quality-gatekeeper`,
  `security-auditor`);
- `/grill-me` + `grilling`, `/handoff` + `handoff`;
- the issue-tracker convention (`.scratch/` layout) and the domain-doc convention
  (`GLOSSARY.md` + `docs/adr/`);
- session hygiene, commit conventions, no-secrets rules, the always-on security lens;
- the optional engineering-skill pack (generic, opt-in).

Project-scoped content that stays in the project that owns it:

- the app and its tests (`dotnet test`, 764 tests today);
- dev-loop scripts (`scripts/dev/*.mjs`), status scripts (`scripts/status*.ps1`),
  UI harnesses (`scripts/ui/*`), fixtures, `/status` and `/activity`, the coverage artifact;
- `CLIMBER_SEED` and the seeded visual-verification loop;
- project UI contract/art direction and the rubric's UI-specific probes 8–13;
- provider/model/permissions in `opencode.json` (machine- and market-specific).

### Verified OpenCode V2 facts that constrain the design

1. `~/.config/opencode/{agents,commands,skills,AGENTS.md}` is discovered for **every**
   project. There is no per-project condition on global `AGENTS.md`.
2. Project `.opencode/*` overrides a same-ID skill and command. Agent definitions **merge**
   (later scalar values win; permission rules append), so a global agent permission can
   never be removed downstream.
3. Global `AGENTS.md` loads unconditionally and is **combined** with project `AGENTS.md`;
   OpenCode does not resolve conflicts between them.
4. A global `skills`/`instructions` array can point at any directory, but explicit `skills`
   entries are registered **above** project `.opencode/skills`, so a global array silently
   outranks a project skill with the same ID.
5. Nested agent/command paths become part of the ID (`agents/team/x.md` → `team/x`).
6. Command `!` shell blocks run outside the agent permission flow.
7. A project `opencode.json` can define agents/commands inline, but the base must not own
   the project's provider/model/permissions.

### The current failure

- `.opencode/README.md` says: "Copy the `.opencode/` directory, `opencode.json`, and
  `AGENTS.md` into a new project." That hand copy is the drift source.
- The partial global install at `%USERPROFILE%\.config\opencode` has four agents that are
  **140 lines behind** the repo, no global `AGENTS.md`, commands or skills, and an
  `opencode.json` missing the model, compaction and permissions.
- Because the global agents share IDs with the repo agents, they **merge** with any
  project-local copy; permissions append. This is a silent behaviour change, not a
  cosmetic staleness.

### Hard constraints

- Windows + PowerShell; the git index is CRLF; use file tools, never `Set-Content`.
- No secrets; the provider key stays `{env:SURPLUS_API_KEY}`. Model IDs are
  market-dependent (`min80`; a non-routable model 404s).
- Base content contains no project names, commands or doc paths: no `dotnet`,
  `scripts/ui`, `CLIMBER_SEED`, `/status`, Razor, or Surplus model choice.
- HillClimberUI must consume the same base with no behavioural regression (floor:
  `dotnet test`; seeded visual check only if the app changes).
- A brand-new project is set up in one documented step.
- The engineering pack stays opt-in and project-scoped.

### Non-goals

Changing the intent of any workflow; adding new workflow content; migrating every existing
project now; changing the app.

---

## 2. Proposals (round 1, verbatim)

### Shape seat — `plan-architect`

> POSITION: Take the chair's mechanism, but demote global from "primary" to "convenience
> tier" and make the committed seam the source of truth. Two corrections to the chair are
> non-negotiable: the global install must write only into OpenCode's default discovered
> dirs and never add `skills`/`instructions` arrays to global opencode.json (explicit
> entries outrank project `.opencode/skills`, silently breaking the override rule the chair
> promises), and base agents must carry zero permissions (project permissions are
> *appended*, so a base permission can never be removed downstream). Global is right
> because it is the only zero-friction path and discovery is free; vendoring is right
> because CI and this repo need a HOME-independent, reviewable pin. Use both, gated.
>
> SHAPE:
> - Base repo: `agents/workflow/*.md`, `commands/{council,hillclimb,grill-me,handoff}.md`,
>   `skills/*/SKILL.md`, `skills/hillclimb/references/hillclimb-rubric.md`,
>   `packs/engineering/`, `tools/install.mjs`, `tools/manifest.json` (per-file sha256),
>   `templates/opencode.fragment.json`, `docs/seam.md`.
> - `node tools/install.mjs --global` writes only manifest-listed files to
>   `~/.config/opencode/{agents,commands,skills}`; `--check` re-hashes and exits 1 on
>   drift; `--project .` vendors into `./.opencode` with a generated header and copies the
>   seam; `--dry-run` prints.
> - Never writes any opencode.json. `--print-config` emits a fragment with
>   `{env:SURPLUS_API_KEY}` and a placeholder model for a human to merge.
> - Seam `.opencode/workflow.json`: `{base, version, workflows[], docs{rubric:"skill:hillclimb"},
>   floor{build,test,harnesses[]}, verify, neverCommit[], packs[]}`. Prose and behaviour
>   stay in AGENTS.md.
> - Universal docs live as skill references, addressed skill-relatively ("the hillclimb
>   skill's references/hillclimb-rubric.md"), never as absolute paths — that is the
>   portability fix. Global AGENTS.md is a ~12-line pointer plus gate: "no
>   `.opencode/workflow.json` ⇒ these agents/commands are dormant."
> - Precedence: project > global, achieved solely via default discovery. Agents namespaced
>   `workflow/task-orchestrator` so the global tier is visibly foreign; commands stay flat
>   as entry points.
> - This repo: thin consumer, vendored at a tag, floor unchanged, CI runs `--project --check`.
>
> WHY: one authoring location; propagation is a hash-verified copy rather than memory; each
> project pins what actually runs, reproducibly without HOME; model and permissions stay
> project-owned so one bad model ID cannot 404 every repo.
>
> COST / RISK: global artifacts still list in every project and the gate is only prose;
> `--check` only catches files the manifest wrote; vendoring reintroduces copies
> (hash-guarded); namespacing agent IDs breaks current invocations once.
>
> WHAT WOULD CHANGE MY MIND: a V2 config key giving agents/commands/skills a discovery
> root with project-over-global precedence — then global needs no copies and vendoring
> drops to CI-only. A probe showing a project skill failing to override a
> globally-installed one would push skills to per-project vendoring by default.

### Experience seat — `quality-gatekeeper`

> **POSITION:** Endorse most of the chair: base as its own repo, universal docs in skill
> references, base never owning `opencode.json`, this repo as thin consumer, pack inside
> base as opt-in. Reject two defaults. (1) Global install as the *default* mechanism — the
> failure we are fixing is a global install drifting 140 lines behind; better tooling on
> the same default reproduces it, silently, and changes every project's behaviour with no
> commit. Committed per-project vendoring should be the default; a global install is an
> opt-in personal defaults layer. The current documented step is literally "copy the
> .opencode/ directory, opencode.json, and AGENTS.md" — that hand copy is the drift source;
> `.opencode/README.md` still says it. (2) The prose gate. Global AGENTS.md loads
> unconditionally, so an opt-in paragraph is always-noise in every non-opted-in project;
> gate in command code instead — commands refuse by default. Also, the seam must own the
> model: all four agents hardcode `model: surplus/deepseek-v4.1-flash-1017#medium`, so a
> fresh non-Surplus project 404s on first use. That is a seam fact, not frontmatter.
> Finally, this repo is the only regression test for the base, so it must *pin* the base,
> not float on whatever global exists.
>
> **EXPERIENCE THESIS:** In an opted-in project, `/hillclimb` starts with no config
> archaeology: the target comes from the seam, the gatekeeper cites the rubric unprompted,
> and the orchestrator never asks where the floor lives. In a non-opted-in project,
> `/hillclimb` prints one line ("workflow not installed here; run `base init`") and does
> nothing else. In a brand-new project, setup is one command plus one readable git diff.
>
> **SETUP:** `base init --vendor .` → pinned agents/commands/skills,
> `.opencode/workflow.json` (target, opt-in, model), managed AGENTS.md block, manifest with
> version. Prints the diff stat and one question if target is missing. Half-filled seam →
> command asks once; unknown schema version → refuses naming the file and line.
>
> **UPDATE:** `base update --check` → `v1.2.0 → v1.3.0, 6 files`, exit 1 in CI. `update` is
> atomic; a failed write leaves files untouched. Never a mixed old/new agent set.
>
> **OVERRIDE:** Project same-ID file wins; `base status` prints per ID: source (project |
> global | base v1.3.0).
>
> **COST / RISK:** N project PRs instead of one install; a new Node tool to trust on
> Windows/CRLF; ad-hoc global projects still drift; pinning is only real if `--check` runs
> in CI.
>
> **WHAT WOULD CHANGE MY MIND:** If V2 surfaced resolved source + version per agent/skill
> *inside* a session, drift would be visible where it bites and global-first becomes
> defensible.

### Risk seat — `security-auditor`

> POSITION: I accept the chair's mechanism but reject two of its controls as insufficient.
> Global install is right — submodules rot and vendored copies are the thing being
> replaced — but a *mutable* `~/.config/opencode` is a global blast radius with no
> per-project pin. Fix: install into versioned, immutable dirs
> (`~/.config/opencode/.workflow/<semver>/`) with thin files at the real paths, and require
> the seam to declare `base: "<semver>"`. Upgrade becomes opt-in per project; rollback is a
> one-line pin flip, not a git revert. Second, the prose opt-in gate in global AGENTS.md is
> a wish, not a control: OpenCode still discovers every global agent, command and skill.
> Opt-in must be structural — install globally only a bootstrap (installer, `doctor`/gate
> skill, no workflow agents), and materialise council/hillclimb/grilling into a project on
> opt-in. Skill-reference docs: agree. Base owns no `opencode.json`: agree, and
> specifically no global `skills`/`instructions` array, since explicit entries outrank
> project skills and would silently win.
>
> TOP FAILURE MODES:
> 1. Stale global changes every project at once. Detect: `--check` hash drift + pin
>    mismatch. Mitigate: versioned store, pin required, no auto-upgrade.
> 2. Override half-merged (agent merge, appended permissions, deleted keys resurrected).
>    Detect: `doctor` renders effective merged config and permissions; marker-canary test.
>    Mitigate: no global permissions at all.
> 3. Malicious or malformed seam — shell blocks run outside permission flow, unknown keys.
>    Mitigate: JSON-schema-validated declarative seam, reject unknown keys, capability names
>    from a base allowlist, never raw command strings.
> 4. Secret leak into base or install. Detect: base-tree scan in `--check`/pre-commit;
>    never echo env. Mitigate: `{env:}` only, `.local.json` excluded from manifest.
> 5. Half-write / Windows paths. Mitigate: stage to temp, parse-validate, single atomic
>    pointer flip; copy not symlink (dev-mode), LF in git, CRLF on write, hash normalised
>    content.
> 6. Uninstall orphans. Mitigate: manifest-driven removal, report (don't blind-delete)
>    unrecorded files.
> 7. Engineering pack = network supply chain. Mitigate: pinned SHA256, no postinstall,
>    diff on bump.
> 8. Base regressions with no floor. Keep install-into-temp integration test, keep
>    `dotnet test` on this repo as consumer.
>
> SAFEST SHAPE: versioned global store + per-project pin + declarative seam +
> bootstrap-only global install. Non-negotiable: pin, schema, `doctor`/`check`, atomic
> flip, manifest.
>
> WHAT WOULD CHANGE MY MIND: evidence that project-level definitions terminally win for
> agents, commands *and* skills and that permissions don't append — then a full global
> install with a prose gate is acceptable. Also if `~/.config` is roaming/shared across
> machines, the blast radius argument gets worse, not better.

---

## 3. Cross-critique (round 2, verbatim)

### Shape seat

> 1. Evidence favours **committed vendoring as the default**, and Risk's own facts settle
> it: discovery reads the real global paths, so a single global artifact set is one active
> version for every project. Risk's versioned store makes the *store* pinnable but not the
> *load* — a project declaring `base: 1.0` still loads whatever thin files sit at the
> discovered paths. Per-project pin therefore requires per-project files. Once files must
> exist, bootstrap-only global + vendored default is strictly simpler and subsumes Risk's
> immutable dirs (the base's release tags are the immutable store). Experience is right
> that "better tooling on the same default" reproduces the bug; Risk is right that the prose
> gate is a wish — both point at: install no workflow agents globally.
>
> 2. Resolve the 404 by removing hardcoded models, not by moving provider config into the
> seam. Base agents ship with **no model** (inherit session); seam carries optional
> `model`; the command preamble/doctor resolves it against configured providers and fails
> in one line naming the seam key and file. Base still never owns `opencode.json` — the
> project does, and the seam only *names* what it expects, with a check instead of a guess.
> Experience's 404 is a doctor bug, not an ownership question.
>
> 3. Revisions: default flips to vendored; global carries bootstrap/doctor only; drop agent
> namespacing (moot); concede schema validation, atomic staged writes, SHA256 on the
> engineering pack, `--check` manifest, and "reject unknown seam keys." Keep: no global
> `skills`/`instructions` array, zero base permissions, docs as skill references.
>
> WHAT THE USER MUST DECIDE: whether non-opted-in projects should see a dormant bootstrap
> with one-line refusal, or nothing at all until vendored.

### Experience seat

> **1. Default: I concede — global-with-pin beats committed vendoring, and Risk's
> versioned immutable store is the reason.** Today's drift was an *unversioned mutable*
> global, not global per se; a semver store plus a seam-declared `base:` pin removes the
> drill (rollback = pin flip, not git revert) and keeps one authoring location. Evidence:
> the hand-copy step in `.opencode/README.md` is what rots, and copy-plus-hash is still a
> copy. Vendoring survives only as the CI/offline escape hatch — the diff a project
> reviews is a one-line semver bump, which is *better* review than 30 vendored files.
>
> **2. Bootstrap-only global wins; namespacing should be rejected.** Namespacing is
> cosmetic — the agents remain discovered and invocable, so it buys no gate and charges a
> breaking rename of the trio. Commands staying flat at least preserves `/council`, but
> then the tier is half-namespaced and the exception is unexplainable. Bootstrap-only is
> honest: non-opted-in projects see nothing to ignore, and `/council` appears exactly when
> the seam does. Its cost — a materialise step — must be one idempotent command, or it's
> the new hand copy.
>
> **3. Neither: the seam owns a model *hint*, `opencode.json` owns the provider.** The 404
> argument is a base-content bug, not an ownership claim — drop `model:` from agent
> frontmatter entirely so agents inherit the project's model, and let `workflow.json` carry
> an optional preferred ID. Provider, key, permissions stay project-owned; base never
> writes `opencode.json`. Malformed/unknown model: refuse at `--check`, not at first token.
>
> **WHAT THE USER MUST DECIDE:** Whether per-project seam pinning with a versioned global
> store is enough reproducibility, or they also need HOME-independent CI installs — which
> decides whether vendoring is a default or an escape hatch.

### Risk seat

> 1. **Default:** committed vendoring wins on evidence. The bug is "a global install
> drifted 140 lines with no commit" — global-with-pin has the same shape and only helps if
> `--check` runs, which dormant projects won't do. Shape's "discovery is free" is true but
> is exactly why it drifts. Narrow the disagreement: global survives as an opt-in
> convenience tier for ad-hoc directories, never as what a fresh project silently inherits.
> This repo must pin, not float (Experience is right).
>
> 2. **Model:** both, split by authority — the seam owns *declared* model/provider/floor;
> `opencode.json` remains the only thing OpenCode obeys, and the base still never writes
> it. Experience's 404 is real: a hardcoded `surplus/deepseek-v4.1-flash-1017` in four
> frontmatters is a seam fact wearing frontmatter clothes. But if the seam can't write
> config, it's decorative unless divergence fails loudly — so `base status`/`--check` must
> diff seam model against effective config and exit non-zero, and `--print-config` emits
> the fragment. Seam = intent, opencode.json = truth, silence between them = defect.
>
> 3. **Revise:** yes, two points. (a) Drop the versioned-store directory scheme — with
> vendoring as default, multiple live base versions per project no longer coexist; a single
> global convenience copy plus manifest hashes and a version *string* in the seam is
> sufficient. That complexity was only justified by global-as-default. (b) Demote my prose
> gate to a minimal pointer, with command-level refusal as the real enforcement —
> Experience is right that an unconditional gate is always-noise.
>
> WHAT THE USER MUST DECIDE: whether a brand-new project gets a committed, reviewable
> workflow diff (vendoring default) or free zero-friction global behaviour that can drift
> unseen.

---

## 4. Experience thesis

In an opted-in project, `/hillclimb` starts with no config archaeology: the target comes
from the seam, the gatekeeper cites the rubric unprompted, and the orchestrator never asks
where the floor lives. In a non-opted-in project, nothing is installed to ignore — and a
stray command prints one line ("workflow not installed here; run `base init`") and stops.
In a brand-new project, setup is one command, a few seam questions, and one readable git
diff that can be reviewed and pinned.

The moment that should feel good: `base update --check` prints exactly what a version bump
would change, and a clean bump is a one-line seam change plus a reviewable file diff.
The friction to avoid: a setup step that silently half-writes, or a workflow that runs with
a half-filled seam and guesses.

---

## 5. Value trade-off register

| Trade | What it buys | What it costs | Who feels it | Reversible? | Position | Strongest counter-argument |
| --- | --- | --- | --- | --- | --- | --- |
| Committed vendoring vs global install as the default | Real per-project pin; reviewable diff; HOME-independent CI; structural opt-in | N project PRs; generated copies (hash-guarded); update is a deliberate step | Owner, per project | Yes (reinstall / eject) | **Vendored default** | A single global install is zero-friction and updates everywhere at once; most of the user's projects are one person's and could share a machine-level version |
| Versioned global store + seam pin | Rollback is a pin flip; one authoring location | One active version is still loaded from fixed paths, so the pin does not change what OpenCode reads; extra machinery | Owner | Yes | **Rejected** | If OpenCode later supports a per-project discovery root, this becomes viable |
| Seam: `.opencode/workflow.json` + managed `AGENTS.md` block | Machine-checkable facts; prose stays readable; one obvious place | Two artefacts to keep honest; a generated block in a human file | Owner, agent | Yes | **Recommended** | An `AGENTS.md`-only seam is simpler and has no schema to drift, but nothing can validate it |
| Universal docs as skill references | Relative paths work; no absolute-path portability problem; travels with the vendored skill | Docs are less discoverable than `docs/agents/*` | Agent, maintainer | Yes | **Recommended** | Project-local copies are easier to read and edit, but they are the drift we are removing |
| Base owns `opencode.json` | One place for provider/model/permissions | Base would bake in a market-dependent model and machine key; one bad ID 404s every project | Owner | Hard to reverse | **Never** | A project could forget to set a model; `doctor` catches it |
| Model in base agent frontmatter | Each agent gets a cheap model out of the box | Breaks on a non-Surplus project; duplicates the project's config | Owner | Yes | **Remove** | A fresh project gets a 404 on first use; the seam hint + doctor is the fix |
| Engineering pack in the base | One source for generic opt-in skills; pack version travels with the base | Base owns a network supply chain; pack choices still project-scoped | Owner | Yes | **Move to base as an optional pack** | Keeping it project-local is less centralised but isolates the supply chain |
| Back up and remove the stale global agents | Eliminates silent merge/append effects | Loses a hand-maintained copy (already 140 lines behind) | Owner | Yes (backup) | **Remove after backup** | Leaving them risks the exact shadowing this task is fixing |
| Base as its own git repo | Single authoring source; tags as the immutable store; this repo can pin it | One more repo to clone and maintain | Owner | Yes | **Recommended** | Keeping the base inside this repo avoids a second repo but ties every project to the app's history |

---

## 6. Options table

| Option | Shape | Cost | Risk | Reversibility | What it buys |
| --- | --- | --- | --- | --- | --- |
| **A. Base repo + committed vendoring (recommended)** | `opencode-workflows` repo; `base init/update/check/doctor/eject`; seam `.opencode/workflow.json` + managed AGENTS block; agents/commands/skills vendored; manifest hashes | N project PRs; one Node tool; generated files | Tool bugs; local edits block updates; copies must be checked in CI | High (eject; re-init) | Real pinning, reviewability, HOME-independent CI, structural opt-in, no global blast radius |
| B. Base repo + global install with versioned store | Global store + thin files + seam `base:` pin | One install; no per-project diff | Pin does not change what is loaded; silent global drift; agent merge/append surprises; global commands in every project | Medium (pin flip, but load path is global) | Zero-friction propagation; one authoring location |
| C. Bootstrap-only global + materialise on opt-in | Global carries installer/doctor only; `base init` materialises per project | A global bootstrap to maintain; two mechanisms | Global bootstrap drifts; materialise step must be idempotent | High | Honest opt-in; no dormant workflow agents; closer to A with a global entry point |
| D. Template repo + hand copy | Copy `.opencode/`, `opencode.json`, `AGENTS.md` | Cheap now | Drift returns; no check; no pin | High | Familiar; no new tool |
| E. Submodule/subtree of the base | `vendor/opencode-workflows` as a submodule | Git friction on Windows; clones need `--recursive` | Discovery does not read the submodule without copies/links; symlinks need developer mode | Medium | Git-native pin |

**Why B fails the pin test.** The versioned store makes the *store* pinnable, but OpenCode
loads `~/.config/opencode/{agents,commands,skills}` — fixed paths. A project's seam saying
`base: 1.0` cannot change what those thin files contain at load time without per-project
files. Per-project files are vendoring. Shape's argument is decisive; Experience's
concession assumes the store changes the load, which it does not.

**Why E fails.** OpenCode discovers `.opencode/agents|commands|skills` only; it does not
read an arbitrary vendored tree. A submodule at `vendor/base` would still need copies or
symlinks into `.opencode/`, and Windows symlinks require developer mode. The copies are the
same as A with extra git friction.

**Why C is not the default.** It is a thin wrapper over A with a global bootstrap that can
itself drift. It becomes attractive later if ad-hoc directories need a one-line entry
point. It does not beat A on pinning, reviewability or opt-in.

---

## 7. Recommendation

**Adopt A: a dedicated `opencode-workflows` repo, consumed by committed vendoring, with a
small `base` CLI and a validated seam.** Specifically:

1. **Base repo** (working name `opencode-workflows`, suggested path
   `C:\Users\joelp\code\opencode-workflows`; its own GitHub repo when the user wants one).
   It contains the universal agents, commands, skills, rubric, conventions, the optional
   engineering pack, the `base` tool, the seam schema and tests. Git tags are the immutable
   version store.
2. **Consumption:** `base init <dir>` vendors a pinned version into
   `.opencode/{agents,commands,skills}`, writes `.opencode/workflow.json`, injects a
   managed block into the project `AGENTS.md`, and writes
   `.opencode/.workflow-manifest.json` (version, commit, per-file SHA-256, block hash).
   `base update` is an explicit version bump; `base check` exits 1 on drift, a missing
   file, a stale version or an invalid seam; `base doctor` validates the seam, model and
   shadowing; `base eject` removes only manifest-owned files and reports the rest.
3. **Opt-in is structural:** no global workflow install. A project without a seam has no
   agents, commands or skills to ignore. A stray command still refuses in one line.
4. **Seam:** `.opencode/workflow.json` carries only machine-checkable facts — project name,
   workflows enabled, build/test/free/paid checks, verify (seed, capture, widths, themes),
   doc paths (issues, glossary, ADR, rubric addendum, contract, art direction),
   never-commit patterns, packs. Prose and standing preferences stay in the project
   `AGENTS.md` outside the managed block. The schema rejects unknown keys.
5. **Authority split:** the base never writes `opencode.json`. The project owns provider,
   model, permissions and compaction. Base agents carry **no `model:`** and **no
   permissions**; they inherit the project model, and project permissions only append.
   `base doctor` warns when no model is configured or when a seam-declared model is absent
   from the effective config, and `base print-config` emits a provider/model fragment for a
   human to merge.
6. **Docs:** the universal rubric and the issue-tracker/domain conventions live in the
   vendored skills as `references/`, addressed skill-relatively. The project's UI-specific
   probes 8–13 move to `docs/agents/hillclimb-addendum.md` and are referenced from the
   seam; the universal rubric stays canonical.
7. **This repo:** becomes a thin consumer. Its universal copies are deleted after parity;
   it keeps its seam, project `AGENTS.md`, `opencode.json`, project docs (UI contract, art
   direction, addendum), dev/status/UI scripts, fixtures, app and tests. The engineering
   pack moves to the base as an optional pack selected by the seam.
8. **Migration:** back up and remove the four stale global agents so they cannot merge with
   the vendored ones. `base doctor` detects same-ID global agents and fails loudly until
   they are gone.

### Dissent

- **Experience seat** holds that global-with-pin should be the default and that a one-line
  semver bump is better review than 30 vendored files. This is recorded, not averaged
  away. The recommendation rejects it on the load-path argument (section 6) and on the
  reproducibility requirement for this repo, which the Experience seat itself accepts.
- **Risk seat** wanted a versioned global store and a bootstrap-only global tier; it
  revised both after the load-path argument, and its remaining controls (schema, manifest,
  atomic writes, doctor, SHA-256 pack pins, no global permissions) are adopted.
- **Shape seat** wanted namespaced global agents; it dropped namespacing once the default
  flipped to vendoring. Its zero-base-permissions and no-global-arrays rules are adopted.

### What the user must decide

1. **Default mechanism:** A (vendored, recommended) vs B (global-with-pin) vs C
   (bootstrap-only global + materialise).
2. **Base repo name and location**, and whether to create a GitHub remote now or later.
3. **Seam shape:** JSON + managed `AGENTS.md` block (recommended) vs `AGENTS.md`-only.
4. **Global cleanup:** back up and remove the four stale global agents (recommended), or
   leave them and rely on `doctor` warnings.
5. **Engineering pack:** move to the base as an optional pack (recommended), or leave it
   project-local.

---

## 8. Thread-split proposal (implementation lanes)

| Lane | Write scope | Depends on |
| --- | --- | --- |
| 1. Base scaffold + tool | New `opencode-workflows/` repo: `tools/base.mjs`, `schema/workflow.schema.json`, `templates/`, `tests/` | — |
| 2. Content extraction | Base repo: agents, commands, skills, rubric, conventions, pack; de-leak project references; add seam references | 1 (schema and skill layout) |
| 3. Consumer migration | This repo: seam, managed `AGENTS.md` block, vendored files, addendum, delete universal copies, update references, global-agent backup/removal | 1, 2 |
| 4. Verification | Base `node --test`; this repo `dotnet test`; `base check`/`doctor`; fresh-repo init walkthrough; `/code-review` | 3 |

Lanes 1 and 2 can run in parallel once the layout is fixed. Lane 3 must wait for both.
Lane 4 is the floor; a red floor cannot ship.

---

## 9. Method and limits

- All three seats ran on the agents' configured
  `surplus/deepseek-v4.1-flash-1017` model. The `council` skill's ideal mix
  (GLM for experience, Sonnet 5 for risk) was not used because model overrides require an
  explicit user request in this harness. Model diversity is a hypothesis, not a guarantee;
  a better brief beats a better model.
- The council did not test the load-path claim against a running OpenCode instance; it is
  derived from the V2 discovery docs (global paths are fixed; project `.opencode` is
  discovered separately). The implementation's fresh-repo init walkthrough is the
  confirming evidence.
- No code, prototype or seed was produced. One artifact: this file.

---

## Decision

Recorded 2026-11-17 after the user answered the five decision questions.

- **Mechanism:** **A — committed vendoring, pinned.** A dedicated base repo is the single
  authoritative source; each project vendors a pinned version into
  `.opencode/{agents,commands,skills,packs,tools}` with a manifest and hash checks.
  No global workflow install; opt-in is structural. B (global-with-pin) and C
  (bootstrap-only global) are rejected for now; C remains a possible later convenience on
  top of A.
- **Base repo:** sibling local git repo at `C:\Users\joelp\code\opencode-workflows`
  (working name `opencode-workflows`), GitHub remote later.
- **Seam:** `.opencode/workflow.json` for machine-checkable facts plus a generated,
  marker-delimited block in the project `AGENTS.md` for prose and triggers. Schema-validated;
  unknown keys rejected.
- **Global cleanup:** back up the four stale agents to
  `%USERPROFILE%\.config\opencode\agents.bak-2026-11-17` and remove them from the discovered
  location. `base doctor` fails on a same-ID global agent.
- **Engineering pack:** move the generic manifest and installer into the base as an optional
  pack, selected per project with `packs: ["engineering"]` in the seam.
- **Authority:** the base never writes `opencode.json`. The project owns provider, model,
  permissions and compaction. Base agents carry no `model:` and no permissions; the
  project config supplies both, and `base doctor` checks them.
- **This repo:** becomes a thin consumer; universal copies are removed after parity, the
  UI-specific rubric probes move to `docs/agents/hillclimb-addendum.md`, and the app,
  scripts, fixtures, project docs and floor stay.