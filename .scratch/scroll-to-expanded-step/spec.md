# Spec: Scroll the output panel to the step a reader expands

Status: approach settled with the user on 2026-11-15, reported immediately after the
[output-panel-readability](../output-panel-readability/spec.md) hillclimb shipped at 8/10.
Implementation not started; the decisions below are locked (AGENTS.md preference 1).

## The report (user's words)

> When expanding a block for a step, the output window should sscroll to focus that step you
> have expanded. Currently when we expand a step that is currently collapsed, there's a
> chance it will expand into overflow, overflow is fine but the scroll window should move
> focus.

## What the code does today (verified, with pointers)

1. **The feed is a reverse scroller with a capped height.** `.log` is
   `display: flex; flex-direction: column-reverse; height: 46dvh; max-height: 520px;
   overflow-y: auto` (`ClimberWeb/Components/Pages/Home.razor.css:320`). In the combined
   stream the live edge is `scrollTop = 0`; engines disagree on the sign when scrolled
   back, so the existing script compares distance instead of sign
   (`atLiveEdge`, `ClimberWeb/Components/App.razor:224`).
2. **Nothing re-anchors the window when a disclosure opens.** Opening any
   `<details>` grows the row and the browser keeps the offset, so the body a reader asked
   for can land outside the panel while the summary — or nothing — stays in view. The
   disclosures are `AiTurn`, `AiReasoning`, `AiTerminalCard` and the trace rows' own
   `<details>`; all four are reachable from the feed.
3. **There is already a toggle hook to build on.** A capture-phase `toggle` listener
   (`App.razor:205`) records `lastOpened` and calls `syncLatest()`. `syncLatest`
   (`App.razor:232`) decides whether the "latest" button shows, which also depends on
   `scrollHeight`, so it must run after any re-anchor too.
4. **A tab switch deliberately restores both scroll and disclosures.**
   `saveTabState`/`restoreTabState` (`App.razor:269`, `:282`) keep them per tab and the page
   calls `climberLog.restoreTab` (`Home.razor`, `RestoreTabAsync`). A new re-anchor must not
   fight that restore, and must not run while it is putting a saved offset back.
5. **Keyboard already works, and only moves focus on close.** Enter opens the focused
   summary; Escape closes the nearest open `<details>` and focuses its summary
   (`App.razor:309`). The reader's turn choice persists per turn key (`_turnOpen` in
   `Home.razor`), so an expanded turn is not re-collapsed by the newest-turn default.
6. **Reduced motion is already respected for animation.** `.ai-*` shimmer/pulse stops under
   `prefers-reduced-motion: reduce` (`ClimberWeb/wwwroot/ai.css`); a smooth scroll must give
   the same reader a jump instead.
7. **Solo subagent tabs read top down.** `isSolo(feed)` (`App.razor:220`) is true there and
   the live edge is the bottom, so any scroll math has to be written for both directions —
   this is the main reason a single CSS rule cannot do it.

## Decisions to settle before coding

1. **What moves.** Recommendation: keep the clicked summary in place and reveal the body
   below it — the reader's eye is already on that line, so moving it would be the surprise.
   The alternative is scrolling the summary to the panel's top edge. Both are one-line
   differences in the math but very different feelings.
2. **What counts as "the step".** A turn is a `<details>`, but so are its reasoning block,
   its terminal card and every trace row. Recommendation: re-anchor for every `<details>` in
   the feed, not just turns — a terminal card with long output has the same problem.
3. **When the block is taller than the panel.** Recommendation: align its summary to the
   near edge (top of the panel while expanding) and let the body scroll, since the body
   cannot fit either way.
4. **On close.** Recommendation: shift the window back by the height that disappeared, so
   the reader is not left on blank space below the collapsed row.
5. **Animate or jump.** Recommendation: `scrollIntoView({ block: "start", behavior })` has
   no "keep the summary where it is" mode; the deterministic version is to measure the
   summary's `getBoundingClientRect()` before and after the toggle and adjust `scrollTop`
   by the delta. Use `behavior: "smooth"` unless `prefers-reduced-motion: reduce`, and only
   when the delta is large enough to matter.
6. **Does focus move too?** Recommendation: no DOM `focus()` move — only the window. The
   summary keeps focus so Enter/Escape keep working; the spec's word "focus" here is the
   reader's attention, not the focus ring.
7. **Live edge behaviour.** While a run is streaming and the reader is at the live edge, a
   new event appends at the live edge. Decide whether an open, older block the reader is
   reading should pin the window against that (likely yes: once they have expanded and been
   re-anchored, they are no longer at the live edge, and the existing "latest" button already
   tracks that). Verify what happens today before choosing.

## Decisions (settled 2026-11-15, with the user)

1. **What moves.** Pin the clicked summary and reveal the body below it. Measure the
   summary's `getBoundingClientRect()` around the toggle and shift `scrollTop` by the delta.
2. **Scope.** Every `<details>` inside `#log-feed`: turns, reasoning blocks, terminal cards
   and trace rows. One capture-phase listener covers all of them, and nesting still shifts
   once per toggle.
3. **Tall blocks and motion.** A block taller than the panel aligns its summary to the near
   edge and lets the body scroll. `behavior: "smooth"` unless
   `prefers-reduced-motion: reduce`, then one jump; deltas under a few pixels are skipped.
   No `focus()` call, so the focus ring stays on the summary and Enter/Escape keep working.
4. **Close and the live edge.** On close, shift by the collapsed height so the summary stays
   where it was and no blank space is left. Do not pin the window against new streaming
   events: once re-anchored the reader is off the live edge, and the existing `latest` button
   already says so. The per-tab restore must not be re-anchored while it is putting a saved
   offset back.

The arithmetic (direction-aware shift, clamping, tall-block alignment, no-op threshold) lives
in a pure service with unit tests; `App.razor` keeps only DOM wiring, beside the existing
toggle listener and tab-state functions.

## Evidence plan

- Implement the measurement as a small pure helper where possible (delta computation is
  arithmetic; a C#/JS-agnostic test can cover the clamp rules) and keep the DOM wiring in
  `App.razor` beside the existing `toggle` listener.
- Seed the panel with temporary sample data covering: a short turn, a turn taller than the
  panel, a terminal card with long output, a reasoning block, and both feed directions
  (combined and solo). One throwaway file plus one hook, deleted before shipping.
- Screenshot before/after and afterwards assert with Playwright: for each disclosure, after
  opening, the summary's rect lies within the feed's rect (with its top inside the panel);
  on close the summary's rect is still inside; scroll offsets are unchanged on load and on
  tab switch; the latest button state stays truthful.
- Keyboard pass: Enter opens, Escape closes and returns focus, with the scroll adjustment
  behaving identically to a click.
- `prefers-reduced-motion: reduce` pass: the adjustment happens in one step, no animation.
- Free floor: `dotnet test` plus the free harnesses (`theme-nav`, `providers`, `security`,
  `polish`). Paid `surfaces.mjs` stays **not run** unless asked.

## Out of scope (until the user says otherwise)

- Changing the panel's height or making it resizable.
- Auto-collapsing other turns when one opens (accordion behaviour).
- Sticky turn summaries or a scroll-position indicator beyond the existing "latest" button.