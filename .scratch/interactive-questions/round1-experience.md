# Experience seat — round 1

## Proposal

A question renders as a `Message` turn whose body is a composition of `Trace` parts: a
`TraceLine` titled with the question, one `TraceDescription` per option, each option a
`Button` row. No new bordered card inside the panel — the contract's "no card inside a
panel" and the art-direction "chrome recedes" both forbid it. It wins because the reader
never leaves the reading grammar of the transcript; it costs that a question is louder
than a tool row, which is correct — it is the live edge. Top risk: it reads as just
another turn and gets missed. Mitigation is the accent: a pending question is the one
place besides the live dot and primary button allowed the accent, and it takes focus.
What changes my mind: if opencode's event carries richer structure (multi-select,
grouped questions) than options-plus-text, a Trace composition will strain and a ported
Questionnaire pattern becomes honest instead of drift.

## Positions

1. **Fidelity vs scope** — smallest answerable surface. Mirror the *semantics* (question,
   options, answer), not the TUI's chrome.
2. **One click vs staged confirm** — staged: select, then confirm. A mis-click submits an
   answer the run acts on; the composer already taught this app that sending is deliberate.
   Default focus lands on the first option, Enter confirms, arrows move.
3. **Free text vs options** — options only for v1, with a visible escape: if none of the
   options fit, the answer is the composer, one turn down. That is a real path, not a
   dead end, and it keeps injection and secrets out of a new channel.
4. **Transport** — adopt `opencode serve` if verification passes; the felt cost of
   `opencode run` is a cancelled question, which is the exact disappointment being fixed.
   Hold-open-stdin is the rejected middle: fragile, untestable.
5. **New surface vs existing rows** — existing rows (`Trace` + `Button`), not a new card.
6. **Answered state** — fold to one quiet line: `asked → answered: <choice>`, a Marker.
   Dead option buttons in a transcript are the anti-pattern "an affordance that only
   appears on hover" inverted: an affordance that can no longer do anything.

## The one thing I would cut

Free text input inside the question surface. Everything else is the felt experience.

## Experience thesis (what an 8/10 must feel like)

- **Three-second read:** at a glance the pending question is the brightest thing below the
  status band — accent edge, sans question at answer weight, options as bordered buttons.
  Nothing else on screen changed weight.
- **First-run path:** run starts → question appears mid-stream → focus moves to the first
  option (stated, not silent) → arrow to a choice, Enter → the row folds to one quiet
  answered line and focus returns to the composer. Keyboard-only, no trap, under ten
  seconds.
- **Copy voice:** the question text is the model's, verbatim. App copy is terminal-quiet:
  `asks:` prefix, never "The assistant would like to know…".
- **One good moment:** answering collapses to `answered: option B` — the machinery steps
  aside the instant it is no longer live.
- **Friction to avoid:** no modal; no re-render that resets a selection mid-stream (open
  state lives in C#, as the contract already requires); no unanswered question persisting
  as answerable in restored history — a restored pending question renders as a closed
  `cancelled` line, never a live form.
