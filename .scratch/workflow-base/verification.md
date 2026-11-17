# Verification — workflow-base

Date: 2026-11-17. Fixed point: `1d84821`. Base: `drax-ai-workflow` v0.1.0 at
`d4eb552` (tag `v0.1.0`), published at
<https://github.com/DraxCodes/drax-ai-workflow>.

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

## Not run

- The Playwright UI harnesses and the token-spending `scripts/ui/surfaces.mjs`
  (no app change; `surfaces.mjs` spends tokens and needs explicit approval).
- No `dotnet test` result is claimed beyond the run above.