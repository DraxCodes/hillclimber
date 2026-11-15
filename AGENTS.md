# Agent notes

## Hillclimb workflow

This repo ships an opt-in hillclimb setup: the `.opencode/agents/` trio
(`task-orchestrator`, `plan-architect`, `quality-gatekeeper`), the `/hillclimb`
command, and the `hillclimb` skill.

When a task is quality-sensitive — UI, UX, visual, copy or docs — or has a clear,
measurable objective (coverage, warnings, build/test time, bundle size, response
time), ask whether to run the hillclimb workflow before implementing it directly.
It is opt-in, so don't assume; a one-line question is enough.

The gate is the 1–10 rubric in `docs/agents/hillclimb-rubric.md`: below 8, name the
specific flaws and iterate; 8 or above, ship unless the task explicitly demands a
10. Tests and build are a floor, not the iterate/stop signal.

## Working preferences

Four standing preferences, chosen deliberately and recorded here so fresh sessions
and handoffs inherit them without re-asking:

1. **Pick the approach before coding.** For any non-trivial feature, settle the
   implementation approach with the user before writing code — for example
   render-only vs. structural rendering, live data vs. a pre-generated artifact, or
   the architecture of a new page. Offer the realistic options with a short
   trade-off each and ask; don't assume. A one-line question is enough.
2. **Verify visual changes with temporary sample data.** When a change is visual,
   start the app seeded with temporary sample data that exercises the new rendering,
   look at it (screenshot via the Playwright harness in the temp `climb-ui` folder),
   then remove the seed. Keep the seed clearly marked, uncommitted, and confined to
   one throwaway file plus a single hook, so it is trivial to delete.
3. **Run the free tests, always.** After any code change, run the full test suite
   (`dotnet test`) and report its real output — never a cached, filtered, or
   remembered result you did not just produce. Tests that spend tokens (the
   Playwright harness under `scripts/ui`, which drives real `opencode` runs) may be
   skipped, but say plainly that they were **not run** rather than implying they
   passed.
4. **Gate hillclimb on perceived quality, not statistics.** Use the 1–10 rubric in
   `docs/agents/hillclimb-rubric.md` as written: score from seeded screenshots and
   a first-run/keyboard read-through; below 8 means name the specific flaws and
   iterate, 8 or above means ship unless the task says `Target score: 10`. Keep
   tests, build and the free harnesses as a floor — a green number never
   substitutes for the rubric.

## Session hygiene

Cost is driven by how much conversation the model re-reads, so keep sessions
short:

- One session per task. Start a fresh session when a task is done rather than
  carrying a long session into a new task.
- Use `/compact` when switching topics mid-session.
- Prefer targeted reads (`grep`, `read` with `offset`/`limit`) over whole files.
- Delegate isolated work to subagents; they get their own context.

## Conventions

- Keep changes small and atomic: one logical change per commit, clear scope.
- Never commit secrets or personal data (`.env`, API keys, `*.pem`, `*.key`,
  `*.local.json`).
- Check `git status` before committing.

## Optional local models

Cloud (`surplus/deepseek-v4.1-flash-1017`) is the default for every agent.
Local models are an opt-in fallback for privacy, offline work, or unlimited
throughput — **not a cost saving**: Surplus prices this model at ~$0.038/M
input, ~$0.50/M output, ~$0.0075/M cached read, so a busy month costs a few
dollars, usually less than the electricity to run a local GPU for the same work.

OpenCode auto-discovers Ollama at `http://127.0.0.1:11434`, so no provider config
is needed. Install Ollama, then pull a model that fits the machine:

```sh
ollama pull qwen3:14b      # ~9 GB, ~30-47 tok/s on a 16 GB card
ollama pull gpt-oss:20b    # ~14.5 GB, fast tool use, shorter context
```

Pulled models appear in `/models` next to the cloud ones. To send one agent to a
local model, set its `model` in a project-level `opencode.json`; leave cloud
everywhere else. Prefer an explicit model entry with a context `limit`, `cost: 0`,
and `capabilities.tools: true`, because discovery may not supply a limit and
compaction depends on it.

Treat the model choice as machine-specific: pick it per project, not in the base.

## Agent skills

### Issue tracker

Issues and specs live as local markdown under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context layout: a root `GLOSSARY.md` and `docs/adr/`. See `docs/agents/domain.md`.
