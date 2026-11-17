---
description: Run the hillclimb loop against a named 1–10 rubric and a test/build floor
agent: task-orchestrator
---

Start a hillclimbing run for the objective below. Follow the `hillclimb` skill
exactly. Read its "Choosing the rubric" section, then pick **one** rubric and
record it before the first score:

1. `Rubric: <name>` in the objective or spec wins.
2. Otherwise the seam's `rubrics.default`.
3. Otherwise `perception`.

The rubrics are `perception` (UI/UX and visual), `usability` (API, CLI, errors,
docs), `performance` (latency, size, cost budgets) and `completeness` (a spec,
proposal or feature that may be missing an obligation). Read the selected file at
the `hillclimb` skill's `references/rubrics/<name>.md`, plus the project addendum
named by the seam.

Objective: $ARGUMENTS

If the objective is vague, first use the `grilling` skill to sharpen it into
settled decisions (skip this when the target and flaws are already clear, to keep
costs down). Then use the `plan-architect` subagent to produce the rubric, the
target score, the evidence plan, the ordered flaws, the floor and the stop
conditions — and, for `performance`, the budget table before any measurement. The
target is 8 unless the objective says `Target score: 10`.

Score every candidate with the `quality-gatekeeper` subagent, from the evidence the
selected rubric requires: the seam's `verify` block for a visual artifact, the
attempt transcript for `usability`, the declared budget and measurements for
`performance`, the obligation inventory for `completeness`. Apply the gate as
written:

- **Below 8:** name the specific flaws holding it back (for example clunky
  transitions, a budget missed at p95, or an undecided failure path), fix those
  flaws, and iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** the seam's `checks` (build, test and free) pass. A red floor cannot score 8
  and cannot ship. A rubric that needs no seed does not get one.

Keep each accepted iteration as its own atomic commit, and revert any iteration the
score does not improve.