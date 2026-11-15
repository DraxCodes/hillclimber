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
the evidence plan, the ordered flaws, the floor and the stop conditions. The
target is 8 unless the objective says `Target score: 10`.

Score every candidate with the `quality-gatekeeper` subagent, from seeded sample
data, screenshots at 900 and 375 in both themes, and a first-run/keyboard
read-through. Apply the gate as written:

- **Below 8:** name the specific subjective flaws holding it back (for example
  clunky transitions, visual clutter, unclear hierarchy), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** a passing build, `dotnet test` and the free assertion harnesses. A
  red floor cannot score 8 and cannot ship.

Keep each accepted iteration as its own atomic commit, and revert any iteration the
score does not improve.
