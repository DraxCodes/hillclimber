# OpenCode base

A small, portable OpenCode setup. Copy the `.opencode/` directory, `opencode.json`,
and `AGENTS.md` into a new project to get a working agent configuration.

## What's in the base

| Path | Purpose |
| --- | --- |
| `opencode.json` | Model, compaction, tool-output limits, and permissions. |
| `AGENTS.md` | Hillclimb prompt, session hygiene, conventions. |
| `.opencode/agents/` | `task-orchestrator`, `plan-architect`, `quality-gatekeeper`. |
| `.opencode/commands/` | `hillclimb`, `grill-me`, `handoff`, `setup-engineering-pack`. |
| `.opencode/skills/` | `hillclimb`, `grilling`, `find-skills`, `handoff`, `context-engineering`. |

The base is deliberately minimal. Engineering and other domain skills are
**not** included — add them per project from the pack below.

## Optional engineering pack

The pack is opt-in and project-scoped. It is described in
[`engineering-pack.json`](engineering-pack.json) and installed with:

```sh
node .opencode/scripts/install-engineering-pack.mjs
```

or in OpenCode with `/setup-engineering-pack`. Installed skills land in
`.agents/skills/`, which OpenCode reads, so they travel with the repo.

| Skill | Source | Why |
| --- | --- | --- |
| `code-review` | `mattpocock/skills` | Two-axis review (Standards + Spec). |
| `codebase-design` | `mattpocock/skills` | Deep-module vocabulary. |
| `improve-codebase-architecture` | `mattpocock/skills` | Architecture scan + HTML report. |
| `setup-pre-commit` | `mattpocock/skills` | Husky + lint-staged + Prettier. |
| `documentation-and-adrs` | `addyosmani/agent-skills` | ADRs, README, API docs. |
| `webapp-testing` | `anthropics/skills` | Browser testing with Playwright. |
| `setup-matt-pocock-skills` | `mattpocock/skills` | Prerequisite: issue tracker + labels. |

Edit the pack by changing the skill list in `engineering-pack.json` and
re-running the script. Update installed skills with `npx skills update`.

## Optional local models

Cloud (`openrouter/deepseek/deepseek-v4.1-flash`) is the default for every agent.
Local models are an opt-in fallback for privacy or offline work — **not a cost
saving**, since this model is already a few dollars a month at typical volumes.

OpenCode auto-discovers Ollama at `http://127.0.0.1:11434`, so no provider config
is needed:

```sh
ollama pull qwen3:14b      # general/agentic, ~9 GB, fits a 16 GB card
ollama pull gpt-oss:20b    # fast tool use, shorter context on 16 GB
```

Pulled models appear in `/models`. To send one agent local, set its `model` in a
project-level `opencode.json`. See `AGENTS.md` for the caveats.

## Notes

- Config changes apply without a restart; OpenCode reloads on save.
- `.opencode/skills/` (native) and `.agents/skills/` (pack) are both discovered.
- Installed pack skills and `skills-lock.json` are generated and gitignored;
  run the pack script after cloning to (re)create them.
- This repo also contains the HillClimberUI sample app, used as the first project
  to adopt the base.
