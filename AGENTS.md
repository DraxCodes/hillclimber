# Agent notes

## Hillclimb workflow

This repo ships an opt-in hillclimb setup: the `.opencode/agents/` trio
(`task-orchestrator`, `plan-architect`, `quality-gatekeeper`), the `/hillclimb`
command, and the `hillclimb` skill.

When a task has a clear, measurable objective — coverage, warnings, build/test
time, bundle size, response time — ask whether to run the hillclimb workflow
before implementing it directly. It is opt-in, so don't assume; a one-line
question is enough.

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

Cloud (`openrouter/deepseek/deepseek-v4.1-flash`) is the default for every agent.
Local models are an opt-in fallback for privacy, offline work, or unlimited
throughput — **not a cost saving**: this model is ~$0.15/M input, ~$0.60/M
output, ~$0.003/M cached, so a busy month costs a few dollars, usually less than
the electricity to run a local GPU for the same work.

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
