# SEALED — brief B answer key

Do not read this before scoring `blind/b-*.md`. It records what actually mattered when
session persistence shipped (commit `8a21e27`, ADR 0002), so the blind read can be scored on
recall and not only on novelty.

## Concerns that materialised

1. **Delete is a two-step inline confirm, always visible, never hover-only.** The rail is the
   phone's session switcher, so a hover affordance is invisible on touch. A modal would
   outweigh a small object.
2. **Delete focus management.** Opening the confirm moves focus to its `delete`; cancelling
   returns focus to the row's own trigger. Without this a keyboard reader tabs through the
   rest of the rail. This was a real defect fixed after the first build.
3. **After a row is deleted, focus must land somewhere sane.** The row disappears; a stranded
   focus is the bug. It lands on `+`.
4. **The live row and the on-screen row carry no delete control.** Their records would be
   written straight back.
5. **Which session am I in.** The on-screen row is emphasised; the live dot stays on the live
   row even while another session is viewed; switching back re-attaches.
6. **A stored `running` row with no live binding reads as `stopped`.** A run cannot outlive
   the process, so it is a crash remnant and must not pulse.
7. **A row never exists with zero messages**, and a row is titled by its first prompt
   (first line, capped).
8. **Session identity is not the browser auth session.** The conversation id is app-minted and
   passed as `opencode --session <id>`; the auth token is a different thing.
9. **Empty state.** Disappears once sessions exist; stays for a genuinely empty store.
10. **Spend is per session, not per turn.** A resumed conversation shows what the whole
    session cost, matching a `this session` label.
11. **Fixture/playback mode stays a display** and writes no files.

## What "high value" means here

Weight 1–4 and 6–7 highest: they are the ones a reader notices immediately (the delete ritual,
the stranded focus, the pulsing ghost row, the wrong-session feeling). 8–11 are correctness
and would be found by a competent architect too, so name them but do not over-credit them.

## Scoring note

The seat is not expected to reproduce ADR 0002. It is expected to predict the *experience*
risks. Credit a concern if it is recognisably one of 1–7, even if phrased loosely.