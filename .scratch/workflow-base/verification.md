# Verification — workflow-base

Date: 2026-11-17. Fixed point: `1d84821`. Base: `drax-ai-workflow` v0.1.0 at
`d4eb552` (tag `v0.1.0`), published at
<https://github.com/DraxCodes/drax-ai-workflow>; re-vendored to v0.2.2 at
`7245da3` and to v0.3.0 at `5448a0b` (see the 0.2.x and 0.3.0 sections below).

> Renamed from `opencode-workflows` to `drax-ai-workflow` on 2026-11-17. The
> internal skill id, AGENTS markers, `WORKFLOW_BASE` env and `base` CLI are
> unchanged, so the workflows behave exactly as before.

This records the evidence the spec's §8 asks for. Commands were run in this
session; nothing here is a remembered result.

## Base repo (`C:\Users\joelp\code\drax-ai-workflow`)

`node --test`:

```
# tests 32
# pass 32
# fail 0
```

Covers the seam schema, hash normalization (CRLF/LF), plan/applier rollback,
`init → check → doctor → update → pack → eject`, unowned-extra reporting, the
manifest version fallback, a missing required doc, a stale `AGENTS.md` block,
`base new` with a preset/`--config`/`--dry-run`/unknown preset, `doctor --fix`
(both dry run and real, including the global-agent backup), the widened secret
scan over `.opencode/tools` and `opencode.json`, and the shipped-content scan for
project terms.

## This repo

`node .opencode/tools/base.mjs check .`:

```
drax-ai-workflow 0.1.0 at C:\Users\joelp\code\HillClimberUI
  clean: 33 files
Clean.
```

`node .opencode/tools/base.mjs doctor .`:

```
· AGENTS.md: managed block matches the manifest
· docs.issues: .scratch
· docs.adr: docs/adr
· docs.rubricAddendum: docs/agents/hillclimb-addendum.md
· docs.contract: docs/ui-contract.md
· docs.artDirection: docs/ui-art-direction.md
· pack engineering: skills installed in .agents/skills
· pack engineering: skills-lock.json present
· opencode.json: project permissions present
· global agents: no same-ID shadowing
· secrets: scanned 34 project text files
! docs.glossary: GLOSSARY.md does not exist (optional)
No errors.
```

`dotnet test --nologo`:

```
Passed! - Failed: 0, Passed: 764, Skipped: 0, Total: 764
```

The four stale global agents were moved to
`%USERPROFILE%\.config\opencode\agents.bak-2026-11-17` before `doctor` could pass.

## Fresh-repo walkthrough

`base new <temp> --project demo --preset web --config --pack engineering`:

- files written: 36 (agents, commands, skills, tools, seam, manifest,
  `AGENTS.md`, the `opencode.json` scaffold);
- seam: `web; 375/900; light/dark`; checks `npm run build` / `npm test`;
- docs: created `.scratch` and `docs/adr`;
- config: one-shot `opencode.json` scaffold written with `<provider/model>`
  placeholders (only because the file was absent);
- `check`: `clean: 33 files`;
- `doctor`: no errors; warnings only for the absent optional `GLOSSARY.md`, the
  not-yet-installed `engineering` pack, and the deliberate placeholders;
- the printed next steps name the placeholder fill, the commit, and the pack
  install hint.

`base new --dry-run` writes nothing, and an unknown preset exits 2 listing the
valid names.

This is the evidence for the opt-in/load-path claim: a fresh project gets the
workflows only from its own checkout, and `opencode.json` stays the project's
after the one-shot scaffold.

## Publication

The base is public at <https://github.com/DraxCodes/drax-ai-workflow>. `main` and
`v0.1.0` both point at `d4eb552`.

Before the first push:

- a read-only security audit scanned every reachable object plus the unreachable
  amended blobs, and found no tokens, keys, credentials, personal email, home path
  or project-specific leak; the only hit was a username in an amended-out blob;
- `git reflog expire --expire=now --all` and `git gc --prune=now` removed the
  unreachable objects, and `git fsck --unreachable` is now empty;
- the public tree has 49 blobs, none of them a `.env`, key, certificate, `.npmrc`
  or credentials file;
- GitHub secret scanning and push protection are enabled, and the secret-scanning
  alert list is empty;
- the project-term content test still passes.

## 0.2.0 — prompt-first bootstrap

Base: `drax-ai-workflow` v0.2.0 at `e961dc7` (tag `v0.2.0`), pushed to `main`.

`node --test` in the base repo:

```
# tests 38
# pass 38
# fail 0
```

New coverage: `new --config` reads the default profile
(`~/.config/drax/bootstrap.json`), writes its `opencode` object with no
placeholders and appends its `agentsMd`; `--no-profile` writes placeholders;
`--profile <file>` uses an explicit file; a literal API key exits 2 before
writing; a missing explicit profile exits 2; `--profile`/`--no-profile` without
`--config` exit 2.

Published-artifact cold start — shallow clone of `v0.2.0` from GitHub, then
`base new <temp> --project cold2 --preset web --config`:

- `files: 33 written`; `config: opencode.json written from the bootstrap profile
  (no placeholders)`; `agents: appended the profile's AGENTS.md section`;
- `check: clean (30 files)`; `doctor: no errors` (only the optional
  `GLOSSARY.md` warning);
- generated config model `surplus/deepseek-v4.1-flash-1017`, agents
  `deepseek#low`, `deepseek#medium`, `glm-5.3-flash#medium`,
  `claude-sonnet-5#medium`; `apiKey` is `{env:SURPLUS_API_KEY}`.

### 0.2.1 — strict credential validation

A read-only security audit of the 0.2.0 diff found two bypasses in
`tools/lib/profile.mjs`: the reference pattern accepted any `{word:anything}`
shape, and only a field literally named `apiKey` was inspected. Both were
reproduced. Fixed in `d397a20` (tag `v0.2.1`): references are now only
`{env:NAME}` or `{file:path}`, and every credential-named field anywhere under
`profile.opencode` is checked. Doctor's backstop uses the same field names.

Base `node --test`: `# tests 41 / # pass 41 / # fail 0`, including both bypasses
and an acceptance case for `{env:}` and `{file:}`.

Live CLI repro against the fixed base:

- `apiKey: "{literal:...}"` → exit 2, `use a {env:NAME} or {file:path}
  reference, not a literal secret`;
- `options.token: "myOpaque..."` → exit 2, same message;
- `apiKey: "{file:./k.txt}"` → exit 0, written as-is.

### 0.2.2 — placeholder and key/bearer bypasses

The re-check of 0.2.1 found two narrower holes, both reproduced:

- a literal wrapped in `<...>` passed because placeholders were accepted for
  credential fields; `{"apiKey":"<myOpaque…>"}` exited 0;
- the credential-name list missed `key` and `Bearer`, so
  `options.key`/`headers.Bearer` literals passed.

Fixed in `7245da3` (tag `v0.2.2`): credential fields accept only `{env:NAME}` or
`{file:path}` — no `<...>` — and the checked names now include `key`, `bearer`,
`passwd` and `auth`. Doctor's backstop matches the same names and no longer
exempts `<...>`.

Live repro against the fixed base (all exit 2, nothing written):

- `options.apiKey: "<myOpaque…>"`;
- `options.key: "myOpaque…"`;
- `options.headers.Bearer: "myOpaque…"`.

Base `node --test`: `# tests 43 / # pass 43 / # fail 0`.

Cold start from the public `v0.2.2` tag with the real machine profile: 33 files,
config written from the profile with no placeholders, `check: clean (30 files)`,
`doctor: no errors`. The generated agents are `deepseek#low`, `deepseek#medium`,
`glm-5.3-flash#medium`, `claude-sonnet-5#medium`, and the key is
`{env:SURPLUS_API_KEY}`.

## Council seats

Surplus catalog checked this session: `deepseek-v4.1-flash-1017`,
`glm-5.3-flash`, `qwen3.8-flash`, `claude-haiku-4.5`, `claude-sonnet-4.6` and
`claude-sonnet-5` are listed; `claude-opus-4.8` is no longer listed, so the seat
table marks it unavailable as of 2026-11-17.

This repo's `opencode.json` now implements the mix the seat table claims:
`quality-gatekeeper → surplus/glm-5.3-flash#medium`,
`security-auditor → surplus/claude-sonnet-5#medium`, chair and shape still on
`surplus/deepseek-v4.1-flash-1017`.

After re-vendoring to 0.2.2:

```
check: clean: 35 files
doctor: no errors (optional GLOSSARY.md warning only)
dotnet test: Failed 0, Passed 764, Skipped 0, Total 764
theme-nav.mjs: FAILURES 0 (3 pages)
security.mjs: FAILURES 0
providers.mjs: 15 checks, 0 failures
```

## Not run

- The token-spending `scripts/ui/surfaces.mjs` (`paid`; needs explicit approval).
- No `dotnet test` result is claimed beyond the runs above.
- The base's `paid` harness was not run.

## 0.3.0 — VS Code Copilot Chat bridge

Base: `drax-ai-workflow` v0.3.0 at `5448a0b` (tag `v0.3.0`), pushed to `main`.

Format evidence (VS Code docs, checked 2026-11-17):

- Copilot reads `AGENTS.md` directly
  (<https://code.visualstudio.com/docs/agent-customization/custom-instructions>).
- Project skills live in `.github/skills/<id>/SKILL.md`; the frontmatter `name`
  must match the directory and be lowercase; skills appear as `/` commands and
  auto-load by description
  (<https://code.visualstudio.com/docs/agent-customization/agent-skills>).
- Custom agents live in `.github/agents/<name>.agent.md` with `name`,
  `description`, `tools` and `user-invocable`
  (<https://code.visualstudio.com/docs/agents/reference/custom-agent-file-reference>).
- Copilot `tools` accepts the aliases `execute`, `read`, `edit`, `search`,
  `agent`, `web`, `todo`
  (<https://code.visualstudio.com/docs/agents/reference/tools-reference>).

Base `node --test`:

```
# tests 49
# pass 49
# fail 0
```

New coverage: the generated bridge validates as Agent Skills (name equals
directory, description present, no `$ARGUMENTS`, no literal placeholder in the
preamble); lens agents carry `read`/`search`, with `execute` on the gatekeeper
and `execute` + `web` on the security auditor; a command/skill id collision
appends the canonical skill body; `new` installs the bridge and `doctor`
reports `copilot: N skills` / `copilot: N agents`; `--no-copilot` on `new` writes
no `.github`; `--no-copilot` on `update` removes the generated tree, prunes the
emptied directories and keeps a project-owned `.github/workflows/ci.yml`; a
project-owned `.github` skill warns instead of failing.

Cold start from the working base with the real machine profile:

```
files: 51 written, 0 removed
check: clean (48 files)
doctor: no errors (optional GLOSSARY.md warning only)
```

Cold start from the **published tag** (`git clone --depth 1 --branch v0.3.0`
from GitHub, `VERSION` = `0.3.0`): the same `51 written`, `check: clean (48
files)`, `doctor: no errors`, and 16 generated bridge files with the three lens
agents. The published artifact, not just the working tree, carries the bridge.

Generated bridge inventory (16 files):

```
.github/agents/plan-architect.agent.md
.github/agents/quality-gatekeeper.agent.md      tools: read, search, execute
.github/agents/security-auditor.agent.md        tools: read, search, execute, web
.github/skills/{start-project,council,hillclimb,grill-me,handoff}/SKILL.md
.github/skills/{context-engineering,find-skills,grilling,workflow-base}/SKILL.md
.github/skills/hillclimb/references/hillclimb-rubric.md
.github/skills/workflow-base/references/{domain,issue-tracker,seam}.md
```

`update --no-copilot` on that project: `files: 1 written, 16 removed`; `.github`
no longer exists; `doctor` reports `copilot: bridge not installed (--no-copilot)`.

This repo, re-vendored from v0.3.0 (the 0.2.2 CLI cannot generate the bridge, so
the update ran the base checkout's `tools/base.mjs`):

```
files: 25 written, 0 removed
check: clean: 53 files
doctor: no errors; copilot: 9 skills; copilot: 3 agents
dotnet build: 0 Warning(s), 0 Error(s)
dotnet test: Failed 0, Passed 764, Skipped 0, Total 764
theme-nav.mjs: crawled 3 pages, FAILURES 0
security.mjs: FAILURES 0
providers.mjs: 15 checks, 0 failures
```

Free-check note: the three `scripts/ui` checks need the dev host, so it was
started with `node scripts/dev/host.mjs start`, the checks ran, and it was
stopped before the commit. `dotnet test` ran with the host stopped, as the
Windows note requires.