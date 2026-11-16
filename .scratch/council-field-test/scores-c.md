# Field test — brief C (risk seat) — blind scores

Scored from `blind/c-1..5.md` with the model labels hidden. Answer key: `key-c.sealed.md`.
Weights R1–R3 carry the design; R4–R6 are correctness a competent auditor reaches; R7–R9 are
posture. A seat that proposes a design instead of a constraint is off-brief.

## Risk recall

| Key risk | c-1 | c-2 | c-3 | c-4 | c-5 |
| --- | --- | --- | --- | --- | --- |
| R1. The reply channel does not exist; the transport is the real cost | ✓ | ✗ | ✓ | ~ | ✓ |
| R2. The answer crosses a process boundary — argv leak or flag injection | ~ | ✓ | ✗ | ✗ | ~ |
| R3. The reply must be bound to the run that asked | ✓ | ~ | ✓ | ~ | ✓ |
| R4. A blocked run needs a timeout and a cancel path | ✓ | ✗ | ✓ | ✓ | ✓ |
| R5. Question and option text are untrusted model output | ✓ | ✓ | ✗ | ~ | ✓ |
| R6. Answers persist, and may contain a secret | ✓ | ✓ | ✓ | ✓ | ✓ |
| R7. Local-only posture | ✓ | ✓ | ✓ | ~ | ✓ |
| R9. Concurrency — a second tab or stale session | ✓ | ~ | ✓ | ~ | ~ |

`✓` named, `~` adjacent, `✗` absent.

## Objective signals

| Signal | c-1 | c-2 | c-3 | c-4 | c-5 |
| --- | --- | --- | --- | --- | --- |
| Weighted coverage (R1–R3) | 2.5 | 1.5 | 2 | 0.5 | 2.5 |
| Unique valid concerns | 3 | 2 | 2 | 1 | 2 |
| Precision (survives triage / raised) | 3/3 | 3/3 | 3/3 | 1/3 | 3/3 |
| Brief adherence (posture + 3 ranked risks + cut) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Evidence quality (file, line, finding) | 5 | 4 | 4 | 2 | 5 |

Unique valid concerns:

- c-1: answering a question is *consent* — a model-shaped "Allow access to `C:\Users\…`?" card
  turns a click into filesystem authority; resume via `--session` instead of holding a process
  open; a pending question that expires must end the run and say so.
- c-2: the sharpest single finding in the set — `CommandLine` shows the quote-and-concatenate
  habit, so an answer containing `"` or `--agent` becomes a flag; and the self-sustaining
  injection loop, where the reader becomes the vector against themselves.
- c-3: the exactly-once state machine (`pending → answered → consumed`) and the hazard of two
  processes interleaving writes to one `--session`.
- c-4: strict schema validation before render and storage. Weakly unique and its rank-1
  framing is speculative.
- c-5: persist the exact question/answer pair immutably so every approval is auditable; do not
  ship the answer endpoint while the cleartext session finding is open.

Precision failures: c-4 assumes "two users on the same LAN both unlock" (the app is
single-user), ranks a speculative JSON-parser failure above the missing channel, and admits
it cannot see `CommandRequest`. c-2's transport paragraph implies the answer channel exists
rather than naming that it does not.

## Subjective signals (1–5)

| Signal | c-1 | c-2 | c-3 | c-4 | c-5 |
| --- | --- | --- | --- | --- | --- |
| Posture clarity | 5 | 4 | 4 | 3 | 5 |
| Risk ranking quality | 5 | 4 | 4 | 2 | 5 |
| Constraint sharpness | 5 | 4 | 5 | 3 | 5 |
| Grounding in the repo | 5 | 4 | 4 | 2 | 5 |

## Ranking

1. **c-1** — covers the three weighted risks, adds the consent framing, no errors.
2. **c-5** — equal depth with the best citations; slightly less explicit on the deadlock.
3. **c-3** — best binding model (exactly-once) and R1, but no injection and no markup risk.
4. **c-2** — the best single finding in the set, but blind to R1, the feature's core cost.
5. **c-4** — speculative rank-1, a single-user premise that is wrong, thinnest grounding.