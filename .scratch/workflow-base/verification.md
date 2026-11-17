# Verification — workflow-base

Date: 2026-11-17. Fixed point: `1d84821`. Base: `opencode-workflows` v0.1.0 at
`cd9a155` (tag `v0.1.0`).

This records the evidence the spec's §8 asks for. Commands were run in this
session; nothing here is a remembered result.

## Base repo (`C:\Users\joelp\code\opencode-workflows`)

`node --test`:

```
# tests 27
# pass 27
# fail 0
```

Covers the seam schema, hash normalization (CRLF/LF), plan/applier rollback,
`init → check → doctor → update → pack → eject`, unowned-extra reporting, the
manifest version fallback, a missing required doc, a stale `AGENTS.md` block, and
the shipped-content scan for project terms.

## This repo

`node .opencode/tools/base.mjs check .`:

```
opencode-workflows 0.1.0 at C:\Users\joelp\code\HillClimberUI
  clean: 32 files
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
· secrets: scanned 23 vendored text files
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

`base init` into a temp directory, then `mkdir .scratch docs/adr`:

- files written: 30 (agents, commands, skills, tools, seam, manifest,
  `AGENTS.md`);
- `check`: `clean: 27 files`;
- `doctor`: no errors; warnings only for the absent optional `GLOSSARY.md` and the
  absent `opencode.json` (the project supplies both);
- discovered tree: `.opencode/agents/*`, `.opencode/commands/*`,
  `.opencode/skills/*`, `.opencode/tools/*`, `.opencode/workflow.json`,
  `.opencode/.workflow-manifest.json`, `AGENTS.md` — the fixed OpenCode load paths,
  with no global install.

This is the evidence for the opt-in/load-path claim: a fresh project gets the
workflows only from its own checkout, and `opencode.json` stays the project's.

## Not run

- The Playwright UI harnesses and the token-spending `scripts/ui/surfaces.mjs`
  (no app change; `surfaces.mjs` spends tokens and needs explicit approval).
- No `dotnet test` result is claimed beyond the run above.