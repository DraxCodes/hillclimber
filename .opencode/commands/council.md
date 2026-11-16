---
description: Deliberate a large feature proposal — options, critiques, experience thesis and value trade-offs before any code
agent: task-orchestrator
---

Run a council on the proposal below. Follow the `council` skill exactly.

Proposal: $ARGUMENTS

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
4. Write `.scratch/<feature>/council.md` and stop.

Do not write code, a prototype beyond the single throwaway probe, or an implementation plan.
End by reporting the recommendation, the dissent, and the questions only the user can answer.