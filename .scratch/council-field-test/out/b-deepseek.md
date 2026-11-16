### 1. Experience thesis

Three-second read: the rail reads as a list of things that happened, not a nav menu. Newest first, the current row marked by a filled bar or the accent, every other row at tertiary weight, 11px uppercase time plus a one-line label ellipsised at the rail edge. Nine rows fit; no cap, it scrolls.

The label is the first prompt, first 40 characters, single line. Never a generated summary — a summary we cannot produce well reads as a guess.

First-run path: the rail stays quiet and the empty state becomes one honest line, "No sessions yet. Runs save here automatically," with `+` the visible primary. After a run ends its row appears at top, and the next prompt continues it — no dialog, no confirm, no "resume?" step. Reopening restores scroll position, the reader's folded state (except the live turn, which stays open), and focus in the composer.

Copy voice: verbs, present tense, no "successfully", no exclamation. The confirm names the session.

The good moment: clicking a past session and finding it exactly as it ended, including the tool rows the reader had opened. That is when it stops being a log.

Friction to avoid: hover-only affordances. Delete must be visible on focus and reachable by keyboard.

### 2. Three highest-value subjective calls

**(a) The live run holds a fixed place.** While a run streams, its row stays pinned at top with the live dot even if the reader opens an older session. Counter: two rows claim attention, and pinning implies the live one is newest. Noticeable: the reader switches away and back without hunting.

**(b) Sort by last activity, not creation.** Continuing a conversation lifts it. Counter: order mutates under the cursor mid-click. Noticeable: the row they just used is at the top, legible at a glance.

**(c) No auto-titles, no previews.** Counter: forty truncated characters will collide. Noticeable: three rows reading "Review the current UI" is the test, and one prompt-first rule beats a title we cannot write.

### 3. The one thing I would cut

Delete on every row. Ship the rail read-only first. Deletion without pruning turns the rail from a place you return to into a place you manage, and it is the one affordance the empty state cannot justify yet.