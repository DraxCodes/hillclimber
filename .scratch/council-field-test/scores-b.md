# Field test — brief B (session persistence) — blind scores

Scored from `blindB/b-1..5.md` with the model labels hidden. Answer key:
`key-b.sealed.md`. Weights 1–4 and 6–7 are the concerns a reader notices immediately;
8–11 are correctness a competent architect would also reach, so they are credited lightly.

## Concern recall

| Key concern | b-1 | b-2 | b-3 | b-4 | b-5 |
| --- | --- | --- | --- | --- | --- |
| 1. Delete is an inline two-step confirm, always visible, never hover-only | ✓ | ~ | ~ | ✓ | ✓ |
| 2. Focus moves into the confirm, cancel returns it to the trigger | – | – | – | – | ~ |
| 3. After the row is deleted, focus lands somewhere sane | – | – | – | – | – |
| 4. Live and on-screen rows carry no delete | – | – | – | – | – |
| 5. On-screen row emphasised; live dot persists when reading elsewhere | ✓ | ✓ | ✓ | ✓ | ✓ |
| 6. A stored `running` row with no live binding reads as stopped | – | – | – | – | – |
| 7. Title = first prompt, capped; no zero-message row | ✓ | ✓ | ~ | ✓ | ✓ |
| 9. Empty state survives only for a genuinely empty store | ✓ | ~ | ~ | ✓ | ✓ |
| 10. Spend is per session, not per turn | – | – | – | – | – |
| 11. Fixture mode writes no files | – | – | – | – | – |

`✓` named, `~` adjacent, `–` absent.

## Objective signals

| Signal | b-1 | b-2 | b-3 | b-4 | b-5 |
| --- | --- | --- | --- | --- | --- |
| Weighted recall (1–4, 6–7) | 2 | 2 | 1.5 | 2 | 2 |
| Unique valid concerns | 2 | 1 | 0 | 2 | 3 |
| Precision (survives triage / raised) | 4/4 | 3/4 | 1/3 | 3/4 | 4/4 |
| Brief adherence (thesis + 3 calls + cut, ≤400w) | ✓ | ✓ (long) | ✓ (thin) | ✓ | ✓ |
| Evidence quality (names a probe, width, screenshot) | 3 | 2 | 2 | 3 | 3 |

Unique valid concerns:

- b-1: a visible cap (newest 50, one footer line, never paginate); selection/scroll
  resetting when a run ends.
- b-2: no ticking spend/elapsed on an unfocused row — the live dot is the whole signal.
- b-3: none that survive triage.
- b-4: nothing auto-selects a row for you; scroll position preserved on reopen.
- b-5: restores the reader's folded state *except the live turn*; the confirm names the
  session; delete visible on focus and keyboard-reachable.

Precision failures: b-3 argues to persist only at run end (loses in-flight work and
contradicts resume); b-4 proposes inline undo, which needs a trash the design does not have;
b-2 calls deletion "reversible-feeling", which is wrong for unrecoverable history.

## Subjective signals (1–5)

| Signal | b-1 | b-2 | b-3 | b-4 | b-5 |
| --- | --- | --- | --- | --- | --- |
| Thesis concreteness | 4 | 4 | 2 | 5 | 5 |
| Copy you would ship | 4 | 3 | 3 | 4 | 4 |
| Trade-off position strength | 4 | 4 | 2 | 4 | 4 |
| Felt read | 4 | 4 | 2 | 5 | 5 |

## Ranking

1. **b-5** — highest unique-concern count, best felt read, no precision failures.
2. **b-4** — equally concrete, one questionable affordance (inline undo).
3. **b-1** — sharpest single call (delete-in-place), thinnest recall.
4. **b-2** — good static-dot call, long, one wrong framing.
5. **b-3** — thin, generic, one harmful recommendation.

b-4 and b-5 are within one point; b-5 wins on unique concerns and precision.