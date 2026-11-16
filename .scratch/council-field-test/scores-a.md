# Field test — brief A (interactive questions) — blind scores

Scored from `blindA/a-1..5.md` with the model labels hidden. No sealed key exists for A:
it is a live brief, so the standard is the published signals — a concern counts as valid if
it names a real constraint from `docs/ui-contract.md`, `docs/ui-art-direction.md` or
`docs/ai-elements.md`, or a real behaviour of opencode's question flow.

## Concern validity

| Concern | a-1 | a-2 | a-3 | a-4 | a-5 |
| --- | --- | --- | --- | --- | --- |
| Must not present as a modal; the feed stays scrollable | ~ | ✓ | ✓ | ✓ | ✓ |
| Do not invent a fourth bordered surface; compose from the transcript's own rows | – | ✓ | ~ | ✓ | ✓ |
| Answered card folds to one quiet row, and the choice stays reviewable | ✓ | ✓ | ✓ | ✓ | ✓ |
| Free text demoted, not a standing second input | ✓ | ✓ | ✓ | ✓ | ✓ |
| Focus lands on the first option, not on a submit | ✓ | ✓ | ✓ | ✓ | ✓ |
| Option labels must wrap, not truncate — a truncated answer misrepresents itself | – | ✓ | ✓ | ✗ | ~ |
| Answer state must survive a re-render | – | – | – | – | ✓ |
| The composer and the card must not be two competing answer paths | – | ✗ | ✓ | – | – |
| Keyboard path is two keys; Enter must not silently answer | ~ | ✓ | ✓ | ~ | ✓ |
| Cap and scroll rules at 375 | ✓ | ✓ | ✓ | – | – |

`✓` named well, `~` adjacent, `–` absent, `✗` a wrong call.

## Objective signals

| Signal | a-1 | a-2 | a-3 | a-4 | a-5 |
| --- | --- | --- | --- | --- | --- |
| Unique valid concerns | 1 | 3 | 3 | 2 | 4 |
| Precision (survives triage / raised) | 3/4 | 3/4 | 4/4 | 3/4 | 4/4 |
| Brief adherence (thesis + 3 calls + cut, ≤400w) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Evidence quality (cites a rule, token or probe) | 3 | 4 | 4 | 3 | 5 |

Unique valid concerns:

- a-1: submit copy matched to the question's verb ("Retry", not "Submit").
- a-2: digits as shortcuts, as in the TUI; an internal 18rem cap; a dead composer reads as
  hijack.
- a-3: the composer becomes the single answer path while a question is open (placeholder and
  send replace the card's submit); Enter selects and moves to submit rather than committing.
- a-4: render as a `Trace`/`TraceLine` composition, not a new card — the "card inside a
  panel" drift the contract names.
- a-5: the answer state must survive a re-render; the fold stays reachable via
  `TraceCollapsible`; the Tab-count test (≤3) for default focus; two keys, no submit detour.

Precision failures:

- a-1: `aria-pressed` toggle buttons for a choose-one group is the wrong semantic — the
  correct one is a radio group — and it invents an `optional` flag the question shape does
  not have.
- a-2: disables the composer, and its own counter ("a dead composer reads as hijack") is the
  stronger argument; the synthesis (a-3) wins.
- a-4: truncates option labels with a title tooltip. A tooltip hides the answer and fails
  touch; wrapping is the correct rule.

## Subjective signals (1–5)

| Signal | a-1 | a-2 | a-3 | a-4 | a-5 |
| --- | --- | --- | --- | --- | --- |
| Thesis concreteness | 4 | 5 | 5 | 4 | 5 |
| Copy you would ship | 4 | 4 | 3 | 4 | 4 |
| Trade-off position strength | 3 | 4 | 4 | 4 | 5 |
| Felt read | 3 | 4 | 4 | 4 | 5 |

## Ranking

1. **a-5** — best evidence quality, four unique concerns, every call carries a real counter
   and a way to notice the difference.
2. **a-3** — the strongest single synthesis (one answer path, the composer) and the sharpest
   keyboard call.
3. **a-2** — very concrete, strong provenance, but one self-defeating recommendation.
4. **a-4** — the best structural call (no new surface), undermined by the truncation rule.
5. **a-1** — concrete but carries the one clearly wrong semantic recommendation and a hedged
   cut.