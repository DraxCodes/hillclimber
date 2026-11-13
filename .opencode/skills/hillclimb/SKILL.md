---
name: Hillclimb
description: Optimise a measurable objective in small, reversible increments that must pass quality gates
---

# Hillclimbing

Use this skill when asked to improve a measurable objective (performance, size,
score, reliability) with a `planner` agent and a read-only `reviewer` gate. The
loop only keeps a change if a gate passes.

## Roles

- **Planner** (`planner` subagent): defines the metric, baseline, quality gates,
  ordered plan, and stop conditions. It does not edit code.
- **Reviewer** (`reviewer` subagent): read-only gatekeeper. Independently
  re-measures and returns `PASS`/`FAIL` per gate. It does not edit code.
- **Orchestrator** (you): runs the loop, makes the changes, and commits.

## Workflow

1. **Frame the objective.** Restate it as a single measurable metric. If it is
   not measurable, stop and agree on a metric first.
2. **Plan.** Launch the `planner` subagent. Capture its metric, baseline, gates,
   plan, and stop conditions. Do not proceed without a numeric baseline.
3. **Baseline.** Record the current metric value and ensure the working tree is
   clean.
4. **Iterate.** For each step in the plan:
   1. Make the smallest change that could move the metric.
   2. Measure locally.
   3. Launch the `reviewer` subagent on the change.
   4. On `PASS`, stage and commit exactly this change as one atomic commit.
   5. On `FAIL`, revert the change and record why. If the same failure repeats,
      move to the stop conditions.
5. **Stop.** Stop when the target is met, an iteration stops improving the
   metric, the budget is exhausted, or a gate fails twice in a row. Report the
   baseline, final metric, and the list of kept commits.

## Rules

- One logical change per iteration, one atomic commit per accepted iteration.
- Never commit secrets or personal data such as `.env`, API keys, `*.pem`,
  `*.key`, or `*.local.json`. Check `git status` before every commit.
- Never let the reviewer or planner modify files; both are read-only.
- Prefer reverting a losing change over debugging it — hillclimbing only moves
  forward on proven improvements.
- Write conventional commit messages scoped to the part of the project changed.
