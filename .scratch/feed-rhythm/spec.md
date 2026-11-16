# Spec: feed rhythm (F7 / R1)

Status: **implemented 2026-11-16; approach A agreed; evidence below.** F7/R1 is the
third and last blocker the Phase 2b gate ordered for the 9/10 push (after F6 and F4).

## The flaw, as the gate stated it

> F7/R1 — feed rhythm. At 900 and 375, completed run: group boundaries are not
> larger than intra-group pitch — Plan step rows ≈25px apart, metadata→next group
> ≈12px, turn blocks ≈20–26px, no Marker or 24–32px gap.

Art direction: "Feed rows 8px apart. Turns separated by 24px or a `Marker` divider,
never by a blank gap alone. The current 9.6px is debt R1."

## What the code did (verified)

`.log` set `gap: 0.6rem` (9.6px) and applied it uniformly to every top-level item,
so turn groups, messages and standalone rows all had the same pitch — and 9.6px is
off the 4px grid. Rows inside a turn already came from `Trace`'s `gap-2` (8px), so
the feed's own pitch was the only thing above the grid.

## Options and decision

- **A. 8px rows + 24px groups (chosen).** `.log { gap: 0.5rem }`; turn and message
  wrappers carry `data-kind`, and `margin-block-start: 1rem` on those items makes a
  group boundary 8px gap + 16px margin = 24px. No new DOM, no scroll-math change,
  and rows inside a turn keep the Trace's 8px.
- B. 8px rows + a `Marker` divider between turns — rejected: adds a visible divider
  to an already dense feed when 24px space already does the grouping.
- C. 8px rows only — rejected: fixes the grid but leaves group boundaries flush with
  rows, the exact flaw the gate named.

## Harness update (deliberate)

`polish.mjs`'s feed check used to demand one uniform gap, which the art direction now
contradicts. It now reads `data-kind` and asserts exactly two levels: 8px for a row,
24px before a group item. A flat pitch still fails, and so does a missing group
margin. The shell-regions check keeps the old uniform-gap rule.

## Evidence

- **Measured (seeded done, `CLIMBER_SEED=1`):** nine feed items at 900 and 375, both
  themes: gaps `8 8 8 8 24 24 24 24`, with every 24 belonging to a `data-kind`
  message/turn. `overflowX: 0`; no console errors.
- **Shots:** `%TEMP%\climb-ui-shots\f7\feed-{900x1200,375x812}-{light,dark}.png`.
- **Seeded polish:** rhythm 56/56 pass. The 16 remaining seeded failures are `tap`
  on inline transcript links (20–22px tall), pre-existing and not rhythm; the
  unseeded floor below is the official gate.
- **Floor (unseeded host):** `dotnet test` 461/0/0; `theme-nav` 0; `security` 0;
  `providers` 15/0; `polish` 100 (0 defects in 560 checks). Paid harnesses not run.

## Not in scope

Inline-link tap targets, the output tab's accent, and the "● completed Run complete."
copy duplication the gatekeeper noted; none is a rhythm defect.