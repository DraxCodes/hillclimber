---
name: handoff
description: "Compact the current conversation into a handoff document for a fresh session"
argument-hint: "What will the next session be used for?"
---

> Copilot Chat: act as the `task-orchestrator` described in
> `.opencode/agents/task-orchestrator.md`. The read-only lenses are the
> `plan-architect`, `quality-gatekeeper` and `security-auditor` custom
> agents in `.github/agents/`. When a step names a skill, read that
> skill's `SKILL.md` under `.opencode/skills/<skill>/` and follow it. The
> project rules are in `AGENTS.md`; the seam is `.opencode/workflow.json`.

Load the `handoff` skill and write a handoff document for the focus below. After the document, always output a copy-pasteable starting prompt for the next session.

the text the user typed after `/handoff`

---

## Reference: the `handoff` skill

Write a handoff document summarising the current conversation so a fresh agent can continue the work. Save to the temporary directory of the user's OS (`$TMPDIR`, else `/tmp`; `%TEMP%` on Windows) - not the current workspace.

Always also reply with a copy-pasteable starting prompt for the next session, in addition to writing the document. Make it a short message the user can paste into a fresh session: point at the handoff file's absolute path, name the focus, and remind the next agent to follow the repo's workflow (spec → implement → `/code-review`). If `AGENTS.md` records standing **Working preferences** (for example "pick the approach before coding" or "verify visual changes with temporary sample data"), fold the relevant ones into the starting prompt so the next session inherits them, and still offer to encode any new recurring preferences durably rather than assuming.

Include a "suggested skills" section in the document, naming which skills the next agent should call the Skill tool for.

Do not duplicate content already captured in other artifacts (specs, plans, ADRs, issues, commits, diffs). Reference them by path or URL instead.

Redact any sensitive information, such as API keys, passwords, or personally identifiable information.

If the user passed arguments, treat them as a description of what the next session will focus on and tailor the doc accordingly.

