### 1. Experience thesis

At three seconds the rail should read as a dated list, not a database: newest row
at top, each row one truncated title line (ellipsis, never wrap to a second line)
plus an 11px uppercase relative timestamp ("14m ago"), tertiary weight. Exactly
one row carries the accent — a still live dot, no ticking numbers — marking the
process's one live run, even when the reader has clicked elsewhere. First-run
path: click a past row, the transcript swaps in place (no reload flash), and
focus lands in the composer textarea, cursor ready, not on the rail item — the
reader should be able to type the next prompt with zero extra clicks. Copy voice
is plain and factual: a message count and a time, never "Session #3" or
marketing filler. The good moment: switching away from the live run and back
again, and finding it exactly where it left off, dot still lit, nothing
re-rendered or lost — continuity proven, not claimed. The friction to avoid:
any confirm-modal for delete that interrupts flow for a reversible-feeling
action, and any row whose affordances (delete, rename) only appear on hover,
which fails at 375px where there is no hover.

### 2. Three highest-value subjective calls

**Title source.** I'd derive the row title from the first user message,
truncated to one line, not "Session N". Counter: a vague first prompt ("fix
this") makes rows indistinguishable, which is the failure mode this was meant to
avoid. A reader notices the difference by scanning the rail and recognizing
content instantly versus having to open three rows to find the right one.

**Live-row motion.** I'd keep the live indicator a static accent dot with no
ticking spend/elapsed in the background row. Counter: without live numbers, a
reader switched away can't tell if the run is progressing or stuck. The
difference shows up as calm versus noisy chrome — a rail of quietly updating
numbers reads as a dashboard, not an instrument, and violates the one-accent,
no-decorative-motion rule.

**Delete affordance placement.** I'd put a small always-visible icon per row
(not hover-revealed), sized down so it doesn't compete with the title. Counter:
always-visible icons add visual weight to every row, and a misclick deletes
unrecoverable history. The difference is felt at 375px: hover-only delete simply
doesn't exist there, so the two approaches diverge completely on mobile, not
just in density.

### 3. The one thing you would cut

Cut per-row background spend/elapsed updates while a session is live but not
focused. Keep the single still accent dot as the entire signal. Live-ticking
numbers in an unfocused row are decoration competing with the one row the
reader is actually reading — exactly the clutter the art direction's hierarchy
and motion rules exist to prevent.
