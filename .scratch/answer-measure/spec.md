# Spec: the answer measure (F4)

Status: **implemented 2026-11-16; approach A agreed; evidence below.** F4 is the
second of the three blockers the Phase 2b gate ordered for the 9/10 push (after F6,
before F7/R1).

## The flaw, as the gate stated it

> F4 — answer measure. At 900, light and dark, any completed run: answer prose spans
> the container (≈730px / 100+ch). `docs/ui-art-direction.md` rule 2: "Prose at
> 60-75 characters. A paragraph that runs the width of a `max-w-3xl` container is
> unfinished, not full-width."

The art direction's "Width" decision already names the answer: "Container stays
`max-w-3xl`. Prose inside it is capped at 68ch; tool output and tables may use the
full width."

## What the code did (verified)

`Markdown.razor` renders every block into `.ai-md`, whose only width constraint is
the message/card column. At 900 the card content is 768px wide and a paragraph ran
the full 768px (≈100+ch at 14px). The user bubble already caps at `max-w-[68ch]`
(`Home.razor`), so only the answer side was unmeasured.

## Options and decision

- **A. Prose-only 68ch cap (chosen).** `max-width: 68ch` on `.ai-md-p`,
  `.ai-md-heading`, `.ai-md-list`, `.ai-md-quote` and `.ai-md-rule`. Code fences and
  tables keep the full column, as the art direction allows. One CSS rule, no markup
  churn, and the cap matches the user bubble's existing width.
- B. Cap the whole `.ai-md` block — rejected: it shrinks code and tables too.
- C. Cap the transcript column — rejected: moves the composer and controls, and
  still does not distinguish prose from tables.

## Evidence

- **Measured (seeded done, `CLIMBER_SEED=1`):** at 900 light/dark a paragraph is
  513px (68ch computed; ≈70 characters) starting at the turn's left edge x=66,
  leaving a 189px right gutter; the container is 768px. At 375 the paragraph is the
  full 343px because the column is already narrower than 68ch, so nothing moves.
  `overflowX: 0`, no console errors.
- **Shots:** `%TEMP%\climb-ui-shots\f4\answer-{900x1200,375x812}-{light,dark}.png`.
- **Scope check:** the cap selects only prose classes; `.ai-md-fence` and
  `.ai-md-table` are untouched and keep their existing full-width grid behaviour.
- **Floor:** `dotnet test` 461/0/0; `theme-nav` 0; `security` 0; `providers` 15/0;
  `polish` 100 (0 defects in 560 checks) against an **unseeded** host. Paid
  harnesses not run.
- **Note:** an intermediate polish run on a mistakenly seeded host reported 16
  rhythm defects at 9.6px. That is F7/R1 (the feed's off-grid pitch), not F4; on
  the unseeded host polish is 100 again.

## Not in scope

F7/R1 (feed rhythm) remains the third blocker; the 9.6px row pitch is measured and
named. No feed, composer or header-band changes here.