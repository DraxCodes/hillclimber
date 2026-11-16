# Chair's proposal — written before any seat ran

Kept out of the seats' reading list so round 1 stays independent.

## Shape

A **transcript row**, not a card. Reuse the existing row primitives: the question as prose at
the answer measure, the options as a radio group of full-width rows, and nothing else. No new
border, no new background, no second composer. The answered state folds to a single line in
the same vocabulary as a tool row, with the options still reachable.

## Transport

This is the whole decision, and it is not a UI decision.

- **Do not hold `opencode run` open and write to stdin.** The invocation is one-shot by
  design; a half-open process that waits for input the app has no documented way to deliver is
  a deadlock with a polite face — frozen transcript, no error, a leaked child.
- **Adopt `opencode serve` only if the question event and the reply are documented and
  verified** against 2.3.0. That is a new transport, a new lifecycle and a new failure mode;
  it should be its own step, behind the existing run seam, not smuggled into a card.
- If neither is verified, **ship nothing that pretends to answer**. Keep today's honest
  cancellation message and make it visible as a row, so the reader knows the run stopped for
  input rather than wondering why the transcript went quiet.

## Positions

1. **Fidelity vs scope:** scope. Mirror the *interaction*, not the TUI's chrome.
2. **One click vs staged:** one click for a listed option. A confirm on every question taxes
   the common case to protect the rare misclick; the transcript keeps the choice auditable, and
   an answer is not destructive.
3. **Free text vs options only:** free text, demoted to a trailing "other…" that reveals a
   field. Refusing free text makes the feature a toy for the one question that matters.
4. **Transport:** verify the server path first; never stdin-hold.
5. **New surface vs existing rows:** existing rows. A fourth bordered shape is the drift the
   contract names.
6. **Answered state:** fold, with the options reachable. Dead forms in a transcript are the
   thing the reader scrolls past.

## What would change my mind

- A verified `--format json` question event plus a documented reply on `opencode serve`
  changes the transport row from a bet to a build.
- A demonstrated stdin protocol on `opencode run` would beat the server (no new lifecycle), but
  only if it is documented, not inferred from the binary's behaviour.
- A real question in this repository's own runs with no listed answer would move free text from
  "demoted" to "first-class".

## The one thing I would cut

The feature's *ambition*, not its surface: if the transport is unverified, cut answering
entirely and ship only the visible "the run asked a question and was cancelled" row. That is
honest, costs almost nothing, and makes the missing capability legible instead of silent.

## Cost and risk

- Cost: if the server path is real, this is the largest change to the run pipeline since
  session persistence — a new process lifecycle, a new event source, a new failure mode.
- Top risk: a model-shaped question becomes a consent surface. A card that says "Allow access
  to `C:\Users\…`?" and a reader who clicks yes has just authorised a filesystem action with
  one click. Questions must never be able to request an app action; they are data sent to a
  model.