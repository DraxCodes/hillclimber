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
