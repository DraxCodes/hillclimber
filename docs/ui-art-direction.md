# Art direction

Status: **in force (2026-11-16)**. Scored by probe 13 of
[`agents/hillclimb-rubric.md`](agents/hillclimb-rubric.md). Provenance
requirements live in [`ui-contract.md`](ui-contract.md).

The contract says the screen is made of the right parts. This file says the parts
are arranged well.

## The look

A dense, quiet instrument. Closer to a trading terminal than a marketing page:
warm off-white or near-black, hairline borders, one accent, no decoration. Mono
only where the reader is reading *data*; sans everywhere they are reading *prose*.
Tight around the machinery, air around the answer.

The test is not "does it look nice" but "would a designer who has shipped tools
recognise this as one of theirs".

## Rules a designer applies by default

1. **Hierarchy before decoration.** Three levels per screen, no more: primary (the
   answer, the live edge), secondary (the machinery: tool calls, reasoning),
   tertiary (metadata: tokens, elapsed, timestamps). If every row is a 14px muted
   line, there is no hierarchy and no amount of colour fixes it.
2. **Measure.** Prose at 60-75 characters. A paragraph that runs the width of a
   `max-w-3xl` container is unfinished, not full-width.
3. **Density with rhythm.** Tight inside a group, generous between groups. The 4px
   grid is a floor, not a style: 8px between rows in a group, 24-32px between
   groups.
4. **Type scale.** Three sizes plus one micro size. Weight carries emphasis;
   colour carries meaning, not emphasis.
5. **One accent.** The accent marks the live state or the primary action. Status
   colours mark outcomes. Nothing else is coloured.
6. **Motion budget.** 120-200ms, ease-out, on disclosure and insertion. Nothing
   loops except the live indicator. `prefers-reduced-motion` removes the motion
   and keeps the meaning.
7. **Chrome recedes.** Borders and background separate things; they do not
   compete. No card inside a card inside a card.
8. **Optical alignment.** Icon column, title column and description column share
   edges. Numbers are tabular. A right-aligned value stays right-aligned at every
   width.

## Decisions for this app

Conservative band. The improvement comes from applying the components properly,
not from departing from them.

- **Type.** Prose in sans at 14px / 1.65. Chrome, commands and data in mono at
  12-13px. Metadata at 11px, uppercase, letterspaced.
- **Rhythm.** Feed rows 8px apart. Turns separated by 24px or a `Marker` divider,
  never by a blank gap alone. The current 9.6px is debt R1.
- **Width.** Container stays `max-w-3xl`. Prose inside it is capped at 68ch;
  tool output and tables may use the full width.
- **Hierarchy in a turn.** The answer is foreground. Tool rows sit one step
  quieter and one step smaller. Reasoning is quietest and starts folded unless
  live.
- **Header.** One primary line: status, method, model. Secondary data (spend,
  tokens, platform) on the second line and visually demoted. Six groups competing
  in one band was the flaw F6 fixed.
- **Accent.** Live dot and the primary button. Nothing else.
- **Radius.** 6px in chrome, 12px on the composer - the one soft object on the
  page.
- **Empty state.** One sentence and three chips, centred, no bordered container
  inside an already-bordered panel.
- **No new colours.** Every value comes from the token map in `app.css`.

## Allowed expressive moves (after the conservative pass)

The conservative decisions above are Phase 2b. These follow it, one at a time,
each re-scored. They depart from the conservative band on purpose, so each needs a
screenshot and a sentence of rationale before it ships.

- A wider transcript, up to 60rem, with the composer matched to it.
- A larger answer scale: 15-16px prose against 12-13px machinery.
- Full-bleed row hover and selection across the transcript column.
- A session rail with presence: real width, the current row emphasised.
- A designed live edge instead of a floating "latest" pill.

**Not allowed, in either band:** new palette values, gradients, glass, shadows
beyond `--shadow`, a second icon set, decorative illustration, or any motion that
loops for attention.

## How it scores

Probe 13 is the tie-breaker. The mechanical probes (1-12) cap the score at 8.

- **8:** clean and correctly composed. Every surface is the right element, every
  state exists, nothing drifts. It may still be visually flat.
- **9:** at three seconds, the primary level is obvious and only it competes for
  attention. Measure, type and rhythm are consistent at 375 and 900, in both
  themes. The machinery is quiet without being invisible.
- **10:** the screen has a point of view: the transcript reads as an instrument
  rather than a form, and a first-time user can name what it is for. No probe finds
  a flaw.

## Anti-patterns

Named so a judge can reject them by name instead of by argument:

- a spinner inside a card inside a panel
- every row at the same weight and opacity
- six groups competing in one header band
- accent colour used decoratively
- monospaced prose
- 9.6px or 0.6rem gaps off the 4px grid
- paragraphs at container width
- a border on every surface
- an affordance that only appears on hover
- status colour used for hierarchy