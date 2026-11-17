---
name: grill-me
description: "Grill me relentlessly to sharpen a plan, decision, or idea"
argument-hint: "[plan, decision or idea]"
---

> Copilot Chat: act as the `task-orchestrator` described in
> `.opencode/agents/task-orchestrator.md`. The read-only lenses are the
> `plan-architect`, `quality-gatekeeper` and `security-auditor` custom
> agents in `.github/agents/`. When a step names a skill, read that
> skill's `SKILL.md` under `.opencode/skills/<skill>/` and follow it. The
> project rules are in `AGENTS.md`; the seam is `.opencode/workflow.json`.

Load the `grilling` skill and interview me about the following until we reach
shared understanding. Follow the skill's round-based format exactly: ask the
whole current frontier of questions in one round, then wait for my answers
before the next round. Do not start acting on the result until I confirm we
have shared understanding.

the text the user typed after `/grill-me`
