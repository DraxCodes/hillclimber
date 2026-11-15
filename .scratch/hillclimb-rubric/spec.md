# Spec: Recalibrate the hillclimb gate to a human-perception rubric

## Context

The `/hillclimb` loop currently decides whether to keep iterating on one thing: a
measurable metric. `plan-architect` demands a numeric metric and baseline, and
`quality-gatekeeper` enforces metric movement, coverage and a clean build. That
works for coverage or bundle size, but it is the wrong test for the thing the loop
is usually pointed at: the output of a prompt as a person experiences it. A screen
can gain passing tests and still be clunky, cluttered or visibly unfinished — and
the loop will happily stop there.

The brief: stop using technical completeness and statistics as the iterate/stop
signal. Use a 1–10 subjective quality rubric about human perception, experience and
finish, with a gate that is explicit about when to keep polishing and when to ship.

## Decisions (settled with the user before implementation)

1. **Rubric location.** One canonical document, `docs/agents/hillclimb-rubric.md`.
   The skill, command, agents and `AGENTS.md` point at it and quote the gate rule
   verbatim; they do not restate the level definitions.
2. **Who scores.** `quality-gatekeeper` scores each candidate from captured
   evidence, read-only. `plan-architect` declares the target score up front.
   `task-orchestrator` makes the change and applies the gate, but cannot raise a
   score. Producer and scorer stay separate.
3. **Evidence.** A score needs temporary seeded sample data, screenshots at 900 and
   375 in both themes, and a short first-run/keyboard read-through. Every named
   flaw or strength says what is visible and where.
4. **Tests/build stay a hard floor.** `dotnet test`, build warnings and the free
   assertion harnesses must pass before any score counts; a red floor cannot reach
   8. `polish.mjs` is supporting evidence, not a threshold: it exits 0 whatever its
   UPS, so only its harness error or a detected run fails it. They no longer decide
   whether to iterate.
5. **Demanding a 10.** Explicit only: `Target score: 10` in the task prompt or
   spec. Otherwise 8 is the ship threshold. At 8–9 with a 10 demanded, the
   gatekeeper names what a 10 requires, the loop gets at most two attempts, then
   stops and reports the ceiling instead of inflating.
6. **Trigger.** `AGENTS.md` asks about running the hillclimb for quality-sensitive
   work (UI/UX, visual, copy, docs) as well as measurable objectives.

## The rubric (summary; canonical text in the doc)

Whole-number levels 1–10, with the user-supplied anchors restated exactly:

- **10 — Shipped Pro Tool:** commercial-grade, high taste, effortless flow, zero
  friction, anticipates needs.
- **8 — Clean & Clear:** ready for external eyes, no rough edges, nothing
  unfinished or "AI-generated".
- **5 — Usable but Clumsy:** works, but clunky, verbose, awkward, or forces mental
  heavy lifting.

Levels 1–4, 6–7 and 9 are defined by observable traits only: missing or misleading
states, invisible focus, dead-end controls, placeholder copy, broken layout at
common widths, inconsistent hierarchy, extra steps, required guesswork. Taste
adjectives are not evidence.

## The gate (verbatim)

- **Below 8:** name the specific subjective flaws holding it back (for example
  clunky transitions, visual clutter, unclear hierarchy), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** a passing build, `dotnet test` and the free assertion harnesses. A
  red floor cannot score 8 and cannot ship.

The score is the only iterate/stop signal. Two consecutive iterations without a
higher score stop the loop; a demanded 10 that stalls reports its ceiling.

## Anti-inflation rules

- Every score cites named evidence. A score without evidence is invalid and is
  treated as below 8.
- An 8+ must name the level-7 flaws that are demonstrably absent.
- A 10 must state that no flaw was found across the probes.
- A below-8 score must name at least one concrete flaw with its location and the
  condition that exposes it.
- Scores are whole numbers. A rising score is recorded per iteration with its
  delta and reason; no movement stops the loop, a drop reverts the change.

## What changes

| File | Change |
| --- | --- |
| `docs/agents/hillclimb-rubric.md` | New canonical rubric: probes, levels 1–10, gate, evidence, anti-inflation, non-screen artifacts. |
| `.opencode/skills/hillclimb/SKILL.md` | Loop recalibrated: rubric/gate decides; metric is a fallback for internal-only work; floor stays. |
| `.opencode/commands/hillclimb.md` | Passes the target score and the rubric to the loop. |
| `.opencode/agents/plan-architect.md` | Produces target score, evidence plan, ordered flaws, floor and stop conditions. |
| `.opencode/agents/quality-gatekeeper.md` | Scores 1–10 with evidence, states why not the next level, enforces the floor. |
| `.opencode/agents/task-orchestrator.md` | Applies the rubric gate; cannot raise a score; stops at target. |
| `AGENTS.md` | Trigger covers quality-sensitive work; new standing preference 4 points at the rubric. |

The engineering pack (`engineering-pack.json`, installer) only installs external
skills and does not own these files, so it needs no edit.

## Out of scope

- Changing the app's own `ClimbService` loop, which measures passing tests for its
  automated runs. This work is about the agent workflow, not the product loop.
- Replacing `scripts/ui/polish.mjs` or adding a numeric floor to it (backlog issue
  05 remains open). Its UPS score is supporting evidence only, not the gate.
- Running the paid `scripts/ui/surfaces.mjs`.

## How we know it is done

1. The rubric doc defines every level 1–10 with observable traits, restating the
   user's 5, 8 and 10 anchors exactly.
2. The gate rule appears verbatim and the floor is explicit.
3. Skill, command, agents and `AGENTS.md` agree with the rubric and contain no
   leftover "measurable metric is the decision" wording.
4. `dotnet test` is green; anything not run is labelled as not run.

## Evidence

- **Tests:** `dotnet test --nologo` → **311 passed, 0 failed, 0 skipped**
  (402 ms), both projects built. This change is documentation and agent prompts;
  no test was added or changed.
- **Not run:** the free browser harnesses (`polish.mjs`, `theme-nav`, `security`,
  `providers`) were **not run** — they need a running server and this change
  touches no UI. The paid `surfaces.mjs` was **not run** and was not needed.
- **Review:** `/code-review 1206434` ran both axes. It found drift between copies
  of the gate, a one-vs-two no-movement contradiction, an unrepresentable ceiling
  verdict, ambiguous evidence ownership, and an uplift on `polish.mjs`. The gate is
  now quoted verbatim in the rubric, `AGENTS.md`, the skill and the command; the
  gatekeeper gained a `STOP` verdict; evidence capture belongs to the orchestrator;
  `polish.mjs` is supporting evidence, not a threshold. The exact 5/8/10 anchors
  were confirmed already verbatim, so they were left untouched.

## Fixed point for review

Session start HEAD is `1206434`. `/code-review 1206434`.
