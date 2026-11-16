# Council seat B — taste

### 1. Experience thesis

The rail is a table of contents, not an inbox. In a three-second read you can name which session is live (accent dot only — rule 5), which one you last touched (topmost non-live row, first line, no weight inflation), and that everything else is older (timestamps at 11px uppercase metadata level). Rows are the machinery level: 12-13px, one title line each, truncating with a single ellipsis — never wrapping to two lines, never shrinking the rail.

First-run path: with no stored sessions, the rail shows the same one-sentence empty state it has today. After a first run finishes, the next launch shows exactly one row; clicking it reopens the transcript scrolled to the bottom with focus defaulting to the composer, so "continue" is just typing. `+` needs no confirm; delete does — one inline undo bar, not a modal.

Copy voice: noun phrases, no marketing. Titles are the run's first prompt, truncated at ~40 characters — never a generated "Session 3" label. Delete reads "Delete", not "Remove session permanently?".

The moment that should feel good: the live row. When a run streams and you switch away and back, it is still there, still pulsing, and the transcript has kept your scroll position. That is the one bit of delight this rail earns.

Friction to avoid: nothing in the rail animates on arrival; nothing auto-selects a row for you; hover must never be the only way to see a delete affordance (an anti-pattern by name).

### 2. Three highest-value subjective calls

**Title = first prompt, no renaming.** Counter-argument: prompts are poor labels ("ok try again"); users will want to name sessions. Difference a person notices: a rail of "ok try again" x4 is unusable at a glance. If, once several sessions exist, titles collide and feel meaningless, revisit — but earn renaming later, don't ship it.

**One live run, shown as a state, not a separate list.** Counter-argument: a pinned "Live" section at the rail's top is clearer. Difference: a pinned section reorders under you when a run starts, which reads as the rail moving itself. If two sessions ever run at once, this call is dead.

**No timestamps in the default row.** Counter-argument: "newest-first" is unverifiable without a date. Difference: dates on every row push density toward noise; relative time ("2m") on the top three, absolute nowhere, keeps tertiary metadata tertiary. If a person cannot tell yesterday's run from last week's, that fails — that is what I would check on a seeded screenshot.

### 3. The one thing you would cut

Cut nothing from scope — cut the rail's ambitions: specifically, delete-with-confirmation as designed friction. Keep delete as a small destructive affordance that asks once via an inline undo, and refuse any richer treatment (modal, trash page, restore UI). "Nothing is auto-pruned" is already a promise of permanence; a second layer of ceremony around deletion would make the rail feel heavier than the app it serves.
