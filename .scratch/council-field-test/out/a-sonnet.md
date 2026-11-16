### 1. Experience thesis

Three-second read: the question is just the next row in the feed, not an interruption — same 68ch measure, same column, no new chrome language. First-run path: the reader's eye lands on the options because default focus goes to the first option, not a "Submit" button — one keypress or click answers it, no tab-hunting. Copy voice: print opencode's question text and option labels verbatim; add no connective tissue ("Please choose one of the following"). One moment that should feel good: on answering, the card doesn't vanish or jar — it settles into a quiet one-line summary ("→ chosen: retry") with the same weight as a collapsed tool row, so the transcript keeps its rhythm. Friction to avoid: a modal overlay that steals focus from the live run, an always-visible free-text field that out-competes the options, and option labels that reflow the card's width — long labels truncate with a title tooltip, same rule as `TraceDescription`, they never grow the row.

### 2. Three highest-value subjective calls

**Inline row vs. modal dialog.** Position: inline, at the feed's live edge, never a dialog. Counter-argument: a blocking question risks going unnoticed if the reader has scrolled. Notice: with `column-reverse` the new question lands at the visual top automatically, so inline costs nothing a modal would fix — a person notices the difference by whether the run ever feels "stuck" waiting on an invisible prompt.

**Collapse after answering vs. keep it open.** Position: collapse to a one-line summary once answered, like a finished turn. Counter-argument: collapsing loses the record of what was actually offered, which matters on review. Notice: a long transcript stays scannable vs. accumulating dead forms a reader has to skip past.

**Free text as demoted vs. equal-weight.** Position: free text is a small secondary toggle ("or type your own"), not a field sitting beside the options. Counter-argument: hiding it makes the escape hatch easy to miss when none of the options fit. Notice: whether the card reads as "pick one" at a glance or as a form with two competing inputs.

### 3. The one thing you would cut

Cut the idea of a dedicated "question card" surface — its own border, background, padding distinct from everything else in the feed. The transcript already has a bubble, a tool row and a marker; a fourth bordered shape is exactly the "card inside a card" drift the contract warns against. Render it as a `Trace`/`TraceLine` composition instead, so a question looks like what it is: one more kind of row in the same instrument, not a form that landed on the page.
