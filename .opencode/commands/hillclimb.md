---
description: Run the hillclimb loop against measurable quality gates
agent: build
---

Start a hillclimbing run for the objective below. Follow the `hillclimb` skill
exactly.

Objective: $ARGUMENTS

If the objective is vague, first use the `grilling` skill to sharpen it into
settled decisions (skip this when the objective is already measurable, to keep
costs down). Then use the **Plan-Architect** subagent to produce the metric,
baseline, quality gates, ordered plan, and stop conditions. Execute the
iterations one at a time, and after each iteration use the
**Quality-Gatekeeper** subagent as the gate before keeping the change. Keep each
accepted iteration as its own atomic commit, and revert any iteration the
Quality-Gatekeeper fails.
