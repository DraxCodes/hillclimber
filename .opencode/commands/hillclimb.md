---
description: Run the hillclimb loop against the 1–10 perception rubric and a test/build floor
agent: task-orchestrator
---

Start a hillclimbing run for the objective below. Follow the `hillclimb` skill
exactly and score against `docs/agents/hillclimb-rubric.md`.

Objective: $ARGUMENTS

If the objective is vague, first use the `grilling` skill to sharpen it into
settled decisions (skip this when the target and flaws are already clear, to keep
costs down). Then use the `plan-architect` subagent to produce the target score,
the evidence plan, the ordered flaws, the floor and the stop conditions.

Score every candidate with the `quality-gatekeeper` subagent, from seeded sample
data, screenshots at 900 and 375 in both themes, and a first-run/keyboard
read-through. Below 8, name the specific flaws and iterate on them. At 8 or above,
stop and ship unless the objective says `Target score: 10`. Keep the passing
build, `dotnet test` and the free harnesses as a floor: a red floor cannot score 8
and cannot ship. Keep each accepted iteration as its own atomic commit, and revert
any iteration the score does not improve.