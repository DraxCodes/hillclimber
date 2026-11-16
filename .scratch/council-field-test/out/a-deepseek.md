### 1. Experience thesis

An 8/10 card is recognisable in three seconds as *the run has stopped and it needs me*:
the header status word changes to the word the card uses, and the card is the only surface
in the feed with controls.

The question is opencode's own string, verbatim, sans at 14px, capped at 68ch, wrapping,
never ellipsised — a truncated question is unanswerable. Options are full-width rows on one
left edge, 8px apart, markers in a fixed column so a two-line option still reads as one row.
Cap: five rows visible at 375, overflow scrolls inside the card, and the card never
exceeds two thirds of the viewport. Default focus is option one, ringed by `:focus-visible` —
an affordance that appears only on hover is already a named anti-pattern.

The good moment: choosing folds the card to a one-line Marker echoing the choice in mono, and
the run streams again in the same breath.

Voice: app chrome in micro mono caps (`answer`); question and options in the app's sans; no
"Oops", no "Please". Friction to avoid: two text fields, or two filled buttons, on one screen.

### 2. Three highest-value subjective calls

**Port Questionnaire; don't invent a row.** Questionnaire is listed unbuilt because opencode
did not emit it; that premise just became false. *Counter:* the element assumes a multi-part
survey with progress, and this is one question and one list, so you port three quarters of it.
*You notice* by where it lands: a surface-map row with an owner, against an EXCEPTION 6 cap.

**Selection is ink, submission is the accent.** *Counter:* without accent on the chosen row,
peripheral vision cannot tell what was picked. *You notice* at 900, where the folded marker's
echo confirms instead.

**Select is not submit.** Pointer click on a row commits; Enter on a focused row only selects
and moves focus to the submit. *Counter:* one gesture, two outcomes. *You notice* the day
someone hits Enter meaning the composer and silently answers option one.

### 3. The one thing I would cut

The free-text field inside the card. The composer is already a textarea inches below;
while a question is open, the composer answers it — its placeholder carries "or type your own
answer", its send replaces the card's submit. One input, one target. If a question arrives
with no options, I would check that first; the composer route must survive it.