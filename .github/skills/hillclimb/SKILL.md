---
name: hillclimb
description: "Run the hillclimb loop against the 1–10 perception rubric and a test/build floor"
argument-hint: "[artifact or objective]"
---

> Copilot Chat: act as the `task-orchestrator` described in
> `.opencode/agents/task-orchestrator.md`. The read-only lenses are the
> `plan-architect`, `quality-gatekeeper` and `security-auditor` custom
> agents in `.github/agents/`. When a step names a skill, read that
> skill's `SKILL.md` under `.opencode/skills/<skill>/` and follow it. The
> project rules are in `AGENTS.md`; the seam is `.opencode/workflow.json`.

Start a hillclimbing run for the objective below. Follow the `hillclimb` skill
exactly and score against the `hillclimb` skill's `references/hillclimb-rubric.md` plus the
project addendum named by the seam.

Objective: the text the user typed after `/hillclimb`

If the objective is vague, first use the `grilling` skill to sharpen it into
settled decisions (skip this when the target and flaws are already clear, to keep
costs down). Then use the `plan-architect` subagent to produce the target score,
the evidence plan, the ordered flaws, the floor and the stop conditions. The
target is 8 unless the objective says `Target score: 10`.

Score every candidate with the `quality-gatekeeper` subagent, from the evidence the seam's
`verify` block declares (seed, capture, widths and themes) plus a first-run/keyboard
read-through. Apply the gate as written:

- **Below 8:** name the specific subjective flaws holding it back (for example
  clunky transitions, visual clutter, unclear hierarchy), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** the seam's `checks` (build, test and free) pass. A red floor cannot score 8
  and cannot ship.

Keep each accepted iteration as its own atomic commit, and revert any iteration the
score does not improve.

---

## Reference: the `hillclimb` skill

# Hillclimbing

Use this skill when asked to improve something a person will experience — UI, UX,
copy, docs, an output panel — or to improve a measurable objective (coverage,
latency, size) with a `plan-architect` agent and a read-only
`quality-gatekeeper` gate. The loop only keeps a change if the gate passes.

## The decision

The gate is the 1–10 rubric in this skill's `references/hillclimb-rubric.md`, plus the
project addendum named by the seam. Read both before the first score; quote them, don't
paraphrase them.

- **Below 8:** name the specific subjective flaws holding it back (for example
  clunky transitions, visual clutter, unclear hierarchy), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** the seam's `checks` (build, test and free) pass. A red floor cannot score 8
  and cannot ship.

The score is the only iterate/stop signal. A green floor does not earn a ship; a number
moving does not earn a ship. A paid check runs only with explicit approval, and is
supporting evidence rather than a threshold unless the seam says otherwise.

## Roles

- `plan-architect` (subagent): declares the target score, the evidence plan, the
  ordered flaws to fix, the floor and the stop conditions. It does not edit code.
- `quality-gatekeeper` (subagent): read-only scorer. Reviews the captured evidence,
  assigns the 1–10 score, names why not the next level, and checks the floor. It
  does not edit code and does not negotiate the score.
- `task-orchestrator` (primary): runs the loop, seeds and captures the evidence,
  makes the changes, applies the gate and commits. It cannot raise a score.

## Workflow

0. **Sharpen (optional).** If "better" is genuinely unclear, load the `grilling`
   skill and interview the user until the target and the flaws are settled. Skip it
   when the task already names what is wrong — grilling costs real tokens.
1. **Frame.** Name the artifact and the target score. The default is 8; only
   `Target score: 10` in the task or spec raises it. Record the target; do not ask.
2. **Plan.** Launch `plan-architect`. Capture the target, evidence plan, ordered
   flaws, floor and stop conditions. Do not proceed without a baseline score.
3. **Baseline.** Capture the evidence the seam's `verify` block declares — seed, capture
   command, widths and themes — or the measurable command for a non-visual target. Have
   `quality-gatekeeper` score the current artifact. Ensure the working tree is clean before
   the first change.
4. **Iterate.** For each step in the plan:
   1. Make the smallest change that targets the top named flaw.
   2. Re-capture the evidence for the affected screens and states.
   3. Launch `quality-gatekeeper` to score the change and check the floor.
   4. On a higher score and a green floor, stage and commit exactly this change as
      one atomic commit.
   5. On an unchanged or lower score, or a red floor, revert the change and record
      why.
5. **Stop.** Stop at the target score, after two consecutive iterations without a
   higher score, when a 10 was demanded and the ceiling is reached, or when the
   budget is exhausted. Report the baseline score, the final score with its named
   remaining flaws, the floor results, and the kept commits. Remove the seed.

## The floor

Before any score counts, run the seam's `checks`: build, test and the free checks. A paid
check runs only with explicit approval. Label anything not run as **not run**; never imply
it passed. The project section of `AGENTS.md` may add nuance (a dev host that must be
stopped first, a check that needs a live server); it may not weaken the floor.

## Rules

- One logical change per iteration, one atomic commit per accepted iteration.
- Never commit the seed sample data. Keep it in one clearly marked throwaway file
  plus a single hook, and delete it before the final report.
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
- `webapp-testing`: seed the app and capture the screenshots a score must cite.
- `code-review`: review the kept commits against a fixed point after the run.
- `find-skills`: discover and install more skills when the loop needs a
  capability this project does not have yet.

All skills inherit the project's default cheap model unless they pin their own,
so prefer leaving skill models unset.

