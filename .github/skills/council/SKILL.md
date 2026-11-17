---
name: council
description: "Deliberate a large feature proposal — options, critiques, experience thesis and value trade-offs before any code"
argument-hint: "[proposal]"
---

> Copilot Chat: act as the `task-orchestrator` described in
> `.opencode/agents/task-orchestrator.md`. The read-only lenses are the
> `plan-architect`, `quality-gatekeeper` and `security-auditor` custom
> agents in `.github/agents/`. When a step names a skill, read that
> skill's `SKILL.md` under `.opencode/skills/<skill>/` and follow it. The
> project rules are in `AGENTS.md`; the seam is `.opencode/workflow.json`.

Run a council on the proposal below. Follow the `council` skill exactly.

Proposal: the text the user typed after `/council`

If the proposal is vague, sharpen it with the user first using the `grilling` skill, then
write the brief. A brief that cannot name the trade-off will waste every seat.

Then:

1. Write the brief — question, fixed point, constraints, non-goals, the trade-off rows, and
   what evidence would settle the decision — and propose your own option before reading the
   seats.
2. Launch the shape (`plan-architect`), experience (`quality-gatekeeper`) and risk
   (`security-auditor`) seats in parallel on the same brief. Round 1 is independent; round 2
   cross-critiques; one revision maximum.
3. Triage into an options table, an experience thesis, a value trade-off register, a
   recommendation with dissent recorded, and a thread-split proposal.
4. Write the council artifact in the seam's issue directory (default
   `.scratch/<feature>/council.md`) and stop.

Do not write code, a prototype beyond the single throwaway probe, or an implementation plan.
End by reporting the recommendation, the dissent, and the questions only the user can answer.

---

## Reference: the `council` skill

# Council

A council is the **divergence** phase for a large feature. It answers three questions the
hillclimb loop cannot:

- should we build this at all, and in which shape;
- what must it *feel* like at 8/10;
- which trade-off do we accept — speed for polish, a click for safety, a dependency for
  reach.

Hillclimb answers "is it built well enough?". Security answers "is it safe?". The user
answers "is it worth it?". A council never decides value on the user's behalf.

## When to convene

Convene when **at least two of these six** hold:

1. a new user-facing surface (screen, panel, card, flow);
2. a new subsystem, protocol, dependency or transport;
3. it crosses a seam (run, session, stream, storage);
4. it is expensive to reverse once shipped;
5. more than one design is credible, not merely possible;
6. the user asks for a council.

Small work skips it. When skipping, say why in one line. A council costs cents; a wrong
shape costs a rebuild, so err toward convening on ambiguous work and never on a rename.

## How often

A council runs **once per decision**, not once per feature build and never per iteration.

- **Once, at the start** of a large feature: the brief, the rounds, the artifact.
- **Again only if the decision materially changed** — the fixed point moved, a constraint was
  invalidated, or the build proved the chosen shape wrong. That is a new brief and a new
  dated section in the same `council.md`, not a re-litigation of the old one.
- **Not during implementation.** "Is it good enough?" belongs to the hillclimb gate, which
  runs on the cheap seats and repeats every iteration. A council that re-runs each time a
  component changes is an expensive linter, not a council.
- **One contested question mid-build** does not need a full council: load this skill for a
  single round with two or three seats. That is what the skill is for.

So the realistic budget for a large feature is one full council (cents) plus, occasionally,
one short single-question round. Against a single hillclimb iteration that drives real
opencode runs, that is noise. The expensive event is building the wrong shape, which is what
the council exists to prevent.

## Roles

The council adds no agents. It reuses the four that already exist, each as a **lens**, and
each is read-only.

| Seat | Agent | Lens | Delivers |
| --- | --- | --- | --- |
| Chair | `task-orchestrator` | owns the brief, rounds, triage and record | one proposal, the options table, the recommendation, the dissent |
| Shape | `plan-architect` | simplest viable shape, seams, reversibility | one proposal and what it defers |
| Experience | `quality-gatekeeper` | the felt read, through the rubric's probes | one proposal and the experience thesis |
| Risk | `security-auditor` | abuse, exposure, failure modes, local-only posture | one proposal or a constraints-first position, plus the red-team list |

The chair proposes its own option first, so it is not only a switchboard.

## Seat models

Seats run on the agents' configured models, so the project decides the mix in
`opencode.json`. Aim for at least two model lineages across the four seats: put the
cheapest reliable model on chair and shape (aggregation and structure), the strongest taste
judgement on experience, and the bluntest model on risk. The project's own `AGENTS.md` may
name a measured mix; use it when it does.

**Verify, do not assume.** A provider can list a model and still fail the request (a
routing floor, a retired version, a market that moved). Make one one-token call before
changing a seat, and record the fallback in the artifact. Model diversity is a hypothesis,
not a guarantee; a better brief beats a better model.

## The brief

The chair writes one brief and every seat receives it verbatim. It must contain:

- the question, in one sentence;
- the fixed point (commit) and the constraints that cannot move;
- non-goals — what is explicitly out of scope;
- what evidence would settle the decision;
- the trade-off rows each seat must take a position on;
- the seat brief and output limit.

A vague brief produces generic advice from every model. If the brief cannot name the
trade-off, stop and sharpen it with the user first.

## Rounds

1. **Independent proposals.** Launch all seats in parallel with the same brief. No seat sees
   another's output. Each returns at most 400 words: shape, why, cost, risk, and what would
   change its mind. The experience seat also drafts the experience thesis.
2. **Cross-critique.** Each seat red-teams the others' proposals. The experience seat states
   what evidence would earn an 8 — it does **not** score, because no artifact exists yet. The
   risk seat names the top failure mode.
3. **One revision, maximum.** Only when two options are within a hair. Never a third round;
   escalate the split to the user instead.

No voting. The chair triages, recommends, and records dissent.

## The experience thesis

One short paragraph: what this must feel like at 8/10, in observable terms. Not adjectives.
Name the three-second read, the first-run path, the copy voice, one moment that should feel
good, and the friction to avoid — concretely, such as "options cap at three with a `more`
row", "focus lands on the first option", "the card is one line wider than an answer".

The experience seat drafts it from the project docs named by the seam
(`docs.artDirection` and `docs.contract`, when they exist) and the rubric in the
`hillclimb` skill. The council critiques it.

**It travels forward.** The chosen thesis is written into the spec, and the hillclimb gate
later scores the built artifact against this thesis *and* the rubric, so the intent is not
re-invented after the fact.

## The value trade-off register

The council must take a position on the trade-offs, not shrug. One row per trade:

| Trade | What it buys | What it costs | Who feels it | Reversible? | Position | Strongest counter-argument |
| --- | --- | --- | --- | --- | --- | --- |

Every row ends in a recommendation **and** the strongest case against it. "It depends" is
not an answer; if the council genuinely cannot settle a row, it is a question for the user,
listed as one.

## Subjective evidence

Some questions cannot be settled in prose — "does this look good?", "is the prettier version
worth the cost?". The council may spend **one throwaway visual probe** per feature:

- two static fragments, same content, differing only in the decision under test (plain HTML,
  or a seeded project page);
- screenshotted at 375 and 900, light and dark;
- chosen by eye and written up with the reason;
- **then deleted.** Never committed, never cited as a deliverable.

Cap it at one screen. If the probe does not settle the question, it goes to the user.

## Output

Write one file, in the seam's issue directory (default
`.scratch/<feature>/council.md`):

1. Question, constraints, non-goals.
2. Proposals — one per seat, unedited.
3. Critiques.
4. Experience thesis.
5. Value trade-off register.
6. Options table — option, shape, cost, risk, reversibility, what it buys.
7. Recommendation, with dissent recorded and rejected options named.
8. Thread-split proposal — lanes with non-overlapping write scopes and their dependencies.
9. Decision — filled in by the user, with the date.

The spec links this file, so the debate is not re-litigated later.

## Hand-off

1. The user decides, or amends.
2. `plan-architect` turns the choice and the experience thesis into the spec and plan.
3. Build. If the feature is quality-sensitive, run the `hillclimb` loop against the thesis.
4. `/code-review`, then one logical commit.

## Rules

- No code, no prototype beyond the single probe, no implementation plan during the council.
  Deliberation and building do not share a phase.
- Every seat is read-only. The chair makes no edits during a council either.
- The experience seat never scores an artifact that does not exist. A score without an
  artifact is void.
- Record dissent verbatim. An option that lost for a good reason is more useful next year
  than a consensus that hid the disagreement.
- Keep cost proportional: a council is cents, but a council on a rename is waste.
- Never commit the probe, the seeded data, or the key of a field test.

