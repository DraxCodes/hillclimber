# Completeness rubric (`completeness`)

Use when the artifact is a **decision**: a spec, a feature proposal, an
architecture choice, or a feature that is already built. The question is not "is
it pretty" (that is `perception`) or "is it fast" (that is `performance`). It is:

> Does this cover what a reasonable expert would expect — including the parts the
> author did not think to mention?

This rubric exists for the case where the point of the hillclimb is to find the
gap you cannot name yet. Its job is to make the unknown unknowns enumerable.

Completeness means **coverage of decisions**, not volume of text. A gap closed by
an explicit, reasoned deferral is closed. A gap the author never saw is not.

The floor, the whole-number rule and the 8-ships rule are unchanged.

## The obligation inventory

Before scoring, enumerate the obligations of this artifact. Each is a row; each
row ends in exactly one state: **done**, **deferred with a reason**, or **gap**.
An empty inventory is itself a flaw — it means the author never looked.

| Axis | Ask |
| --- | --- |
| Actors | Who uses, operates, pays for, attacks or is affected by this? Is each served or explicitly excluded? |
| Entry points | Every way in: UI, API, CLI, event, cron, import, admin. Is each handled or excluded? |
| Inputs | Missing, empty, zero, one, many, huge, malformed, duplicated, unicode, concurrent. |
| States | Empty, loading, success, partial, error, timeout, disconnected, retry, cancel. |
| Permissions | Who may read, write, delete, and who is denied? Is denial visible and safe? |
| Data lifecycle | Create, read, update, delete, expire, archive, restore, export. What happens to old data? |
| Failure | What fails, how it is detected, what the user sees, what the operator sees, and whether retry is safe (idempotency). |
| Limits | Rate, size, quota, timeout, payload, concurrency, retention. What happens at the limit? |
| Observability | Logs, metrics, traces, alerts. Could an operator tell this is broken at 3am? |
| Compatibility | Existing data, clients, config, versions. Is there a migration, and a rollback? |
| Cost | Compute, storage, money, operator time. Is the expensive path named? |
| Security & privacy | Trust boundary, secrets, authorization, personal data, audit. |
| Dependencies | New library, service, protocol or vendor. What is the exit if it fails? |
| Non-goals | Is each non-goal explicit, and consistent with what the artifact actually does? |

Add project-specific axes from the seam's `docs.contract`, `docs.rubricAddendum`
and the project section of `AGENTS.md`. Remove an axis only with a one-line reason.

## How to look

1. **Trace the requirements.** Every stated requirement maps to a decision or a
   test. An orphan requirement, or an untraced claim, is a gap.
2. **Hostile reviewer.** What would a skeptical senior engineer attack first?
   Answer it in the artifact or record it as an accepted risk.
3. **New engineer.** Could someone implement or operate this from the artifact
   alone? Where they would have to ask, name the missing decision.
4. **Operator at 3am.** It is broken. What do they see, what do they do, and how do
   they know when it is fixed?
5. **Security and compliance.** What is the trust boundary, what is logged, what is
   personal, what is the audit trail, what is the blast radius of a mistake?
6. **Reverse the feature.** What breaks if this is removed or rolled back? Is that
   documented?
7. **Alternatives.** For each expensive or irreversible choice, is there a stated
   reason and a named alternative that lost? A decision with no alternative is a
   guess.
8. **Definition of done.** For each obligation marked done: the test or acceptance
   criterion that proves it, including the failure cases.

## Levels

Anchored on how much of the obligation surface is decided.

### 10 — Nothing to surprise us

- Every inventory row is done or explicitly deferred with a reason; no row is
  blank.
- Every probe answered; hostile, new-engineer, operator and security questions all
  have written answers.
- Every irreversible decision names its alternative and why it lost.
- Each obligation has an acceptance criterion, including failure and boundary
  cases.
- The reviewer, reading as an outsider, cannot name a question the artifact does
  not answer.

### 9 — One named edge

- The full inventory is decided.
- Exactly one named, low-exposure gap remains: a rare failure path, a deferred
  hardening item, or a boundary case with a stated cost.
- If the reviewer cannot name it, score 10; if it sits on the primary path, it is
  7 or lower.

### 8 — Complete for the primary path

- Every primary-axis row is done or deferred with a reason: actors, entry points,
  inputs, states, failure, permissions, observability.
- No silent scope: what is out is written as a non-goal.
- At most one named gap remains, off the primary path, with its cost stated.
- A new engineer could implement the main path without asking a blocking question.

### 7 — One real gap off-path

- The primary path is fully decided.
- One obligation a reviewer would expect is undecided or unstated — a limit, a
  migration, an error path, an operator signal — with a named cost.
- Everything else meets level 8.

### 6 — Decided the happy path

- The main path is complete and implementable.
- Two or three secondary obligations are missing or hand-waved: retries, migration,
  quota, logging, permissions, rollback.
- An implementer would have to ask two or three questions, none blocking.

### 5 — A blocking question exists

- The primary path is described but an implementer must make a decision the
  artifact should have made: the data model, the error contract, the authorization
  rule, or what happens at the limit.
- The happy path is clear; the edges are not.

### 4 — Missing an axis

- A whole inventory axis is unaddressed — for example failure handling, permissions,
  or data lifecycle.
- The artifact can be started but not finished without significant rework.

### 3 — Proposal, not a decision

- Major obligations are unstated across several axes.
- The artifact states goals and leaves the decisions to implementation, or
  contradicts itself on scope.

### 2 — Under-specified

- The primary path itself is ambiguous; two implementers would build different
  things.
- Non-goals are absent, so scope is unbounded.

### 1 — Nothing decided

- The artifact cannot support a decision or an implementation.
- Or it claims completeness while leaving every obligation unstated.

## Evidence a score must cite

A score without this evidence is treated as below 8.

- **Inventory:** the filled table, with one state per row. A score without the
  inventory is void.
- **Gaps:** each states the obligation, where it belongs, and the cost of leaving it
  open.
- **Probe answers:** the hostile, new-engineer, operator and security answers, or a
  recorded deferral.
- **Trace:** the requirements-to-decision mapping, and any orphan.
- **Floor:** the exact commands run and their observed result; anything not run is
  labelled "not run". A spec with no build/test says so explicitly.

Scorecard shape:

```text
Rubric: completeness
Artifact: <spec | proposal | feature>
Score: 7/10 — One real gap off-path
Target: 8
Inventory: <rows>; gaps: <n>; deferred: <n>
Gaps holding it at 7:
  1. <obligation> — <axis> — <cost if left open>
Why not 8: <the level-8 trait that is missing>
Why not 6: <the level-6 trait that is absent>
Floor: <command> <result>; or "no build/test in this project (stated)"
```

## Anti-inflation rules

- A completeness score without a filled inventory is void.
- "Deferred" requires a reason and a cost; an unexplained deferral is a gap.
- Do not reward length. A short spec that decides every row beats a long one that
  decides none.
- Do not count a restated goal as a decision.
- A silent non-goal is a gap even when the behaviour is defensible.
- Two consecutive iterations without a higher score stop the loop; the next move
  is a decision from the user, not another rewrite.