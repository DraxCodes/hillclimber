---
description: Turn a new project idea into an approved first spec before any code
agent: task-orchestrator
---

Read `AGENTS.md` and `.opencode/workflow.json`. Confirm the seam's checks, the doc
paths and the packs before you ask anything.

Idea: $ARGUMENTS

If the idea does not already answer them, ask for:

1. the purpose in one paragraph;
2. the first milestone — the smallest thing worth shipping;
3. the non-negotiables (stack, hosting, privacy, budget, deadline);
4. explicit non-goals.

Then:

- If at least two council triggers hold, say which two in one line and stop for
  approval to run `/council`. Do not implement.
- Otherwise write a spec in the seam's issue directory (`docs.issues`, default
  `.scratch/<slug>/spec.md`). Follow the `workflow-base` issue-tracker format. It
  must contain: goal, non-goals, the seam's floor, the evidence that counts as
  done, the experience thesis if a council ran, and the first three tasks.
- Do not scaffold the stack, install dependencies, or write app code. Stop and ask
  me to approve the spec.

After approval, start task 1 and prove it with the seam's checks.