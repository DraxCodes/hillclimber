---
name: Hillclimb
description: Improve an artifact's quality in small, reversible increments, gated by a named 1–10 rubric (perception, usability, performance or completeness) with a test/build floor
---

# Hillclimbing

Use this skill when asked to improve something a person will experience — UI, UX,
copy, docs, an output panel — a measurable objective (coverage, latency, size), or
a decision that may be missing an obligation (a spec, a proposal, a feature). The
loop names a rubric, plans with a `plan-architect`, and keeps a change only when
the read-only `quality-gatekeeper` gate passes.

## Choosing the rubric

The gate is **one** named rubric in `references/rubrics/`, plus the project
addendum named by the seam. Choose it once, before the first score, and record
the choice. The selection order:

1. An explicit `Rubric: <name>` in the task or spec wins.
2. Otherwise the seam's `rubrics.default` (for example
   `"rubrics": { "default": "performance" }`).
3. Otherwise `perception`.

| Rubric | Artifact | Question |
| --- | --- | --- |
| `perception` | UI, UX, a visual surface, copy, a rendered document | Does it land well with a person looking at it? |
| `usability` | API, CLI, errors, logs, config, docs, notifications | Can a first-time user get value without guessing? |
| `performance` | Latency, throughput, memory, size, build time, cost | Does it meet a budget declared before measuring? |
| `completeness` | A spec, proposal, architecture choice, or a built feature | Is every obligation decided, including the ones nobody named? |

Pick the rubric for the artifact, not for the project. A UI project can run a
`performance` hillclimb on its build; a backend project can run `completeness` on
a spec. If none fits, say so, state why, and use the closest one rather than
inventing a silent scale. A project may add its own rubric file and name it in
`rubrics.default`.

Read the chosen rubric and the addendum before the first score; quote them, don't
paraphrase them.

## The decision

Every rubric uses the same gate:

- **Below 8:** name the specific flaws holding it back (for a UI, clunky
  transitions; for a spec, an undecided failure path), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** the seam's `checks` (build, test and free) pass. A red floor cannot
  score 8 and cannot ship. A paid check runs only with explicit approval and is
  supporting evidence, not a threshold, unless the seam says otherwise.

The score is the only iterate/stop signal. A green floor does not earn a ship; a
number moving does not earn a ship.

## Roles

- `plan-architect` (subagent): declares the rubric, the target score, the evidence
  plan, the ordered flaws to fix, the floor and the stop conditions. For
  `performance` it also declares the budget table before any measurement. It does
  not edit code.
- `quality-gatekeeper` (subagent): read-only scorer. Reads the selected rubric,
  reviews the evidence, assigns the 1–10 score, names why not the next level, and
  checks the floor and the rubric's anti-inflation rules. It does not edit code and
  does not negotiate the score.
- `task-orchestrator` (primary): runs the loop, seeds and captures the evidence,
  makes the changes, applies the gate and commits. It cannot raise a score.

## Workflow

0. **Sharpen (optional).** If "better" is genuinely unclear, load the `grilling`
   skill and interview the user until the target and the flaws are settled. Skip it
   when the task already names what is wrong — grilling costs real tokens.
1. **Frame.** Name the artifact, the rubric (see above) and the target score. The
   default is 8; only `Target score: 10` in the task or spec raises it. Record the
   rubric and the target; do not ask.
2. **Plan.** Launch `plan-architect`. Capture the rubric, target, evidence plan,
   ordered flaws, floor and stop conditions. Do not proceed without a baseline
   score.
3. **Baseline.** Capture the evidence the chosen rubric requires. For a visual
   artifact that is the seam's `verify` block (seed, capture, widths, themes); for a
   non-visual one it is the attempt transcript, the budget measurement or the
   obligation inventory. Have `quality-gatekeeper` score the current artifact.
   Ensure the working tree is clean before the first change.
4. **Iterate.** For each step in the plan:
   1. Make the smallest change that targets the top named flaw.
   2. Re-capture the evidence the rubric requires for the affected artifact.
   3. Launch `quality-gatekeeper` to score the change and check the floor.
   4. On a higher score and a green floor, stage and commit exactly this change as
      one atomic commit.
   5. On an unchanged or lower score, or a red floor, revert the change and record
      why.
5. **Stop.** Stop at the target score, after two consecutive iterations without a
   higher score, when a 10 was demanded and the ceiling is reached, or when the
   budget is exhausted. Report the rubric, the baseline score, the final score with
   its named remaining flaws, the floor results, and the kept commits. Remove the
   seed.

## The floor

Before any score counts, run the seam's `checks`: build, test and the free checks. A paid
check runs only with explicit approval. Label anything not run as **not run**; never imply
it passed. The project section of `AGENTS.md` may add nuance (a dev host that must be
stopped first, a check that needs a live server); it may not weaken the floor.

## Rules

- One logical change per iteration, one atomic commit per accepted iteration.
- Never commit the seed sample data. Keep it in one clearly marked throwaway file
  plus a single hook, and delete it before the final report. Non-visual rubrics
  have no seed; do not invent one.
- Record the rubric and the target before the first score; neither moves during a
  run. A rubric chosen after seeing the result is not a gate.
- Never let `quality-gatekeeper` or `plan-architect` modify files; both are
  read-only.
- No score without named evidence. A score without evidence is treated as below 8.
- Prefer reverting a losing change over debugging it — the loop only moves forward
  on a higher score with a green floor.
- Respect "8 or above ships": do not keep polishing toward a 10 the task did not
  ask for.
- Write conventional commit messages scoped to the part of the project changed.
- Never commit secrets or personal data such as `.env`, API keys, `*.pem`,
  `*.key`, or `*.local.json`. Check `git status` before every commit.

## Related skills

- `grilling`: sharpen a fuzzy objective into settled decisions before planning.
  Optional and token-heavy — use it only when the goal is genuinely unclear, to
  keep costs down. Invoke it with `/grill-me`.
- `webapp-testing`: seed the app and capture the screenshots `perception` scores
  must cite.
- `code-review`: review the kept commits against a fixed point after the run; it
  complements `completeness` without replacing it.
- `find-skills`: discover and install more skills when the loop needs a
  capability this project does not have yet.

All skills inherit the project's default cheap model unless they pin their own,
so prefer leaving skill models unset.
