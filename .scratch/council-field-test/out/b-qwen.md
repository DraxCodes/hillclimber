### 1. Experience thesis

The rail should read like a terminal's status strip: a column of nouns, one of them alive. At three seconds: current row emphasised, live row carrying the accent dot, nothing else either. Titles are one line, sans, ellipsised at the tail, never wrapped; under each, 11px uppercase meta — relative time, model.

First-run path: the empty rail keeps today's honest sentence. The first prompt turns it into the top row, selected without a click; insertion is the only motion, 120–200ms ease-out. Reopening loads transcript and composer together, default focus in the composer: the job is the next prompt. Render the newest 50, named in one footer line; never paginate.

Copy voice: verbs, no anthropomorphism — "New session", never "Welcome back!".

The moment that should feel good: reopening an old session while another runs — its dot pulsing, new rows landing as output arrives.

Frictions to avoid: any switch that quietly stops a run, the one bug the rail promises can't exist; selection or scroll resetting when a run ends; hover-only affordances.

### 2. Three highest-value subjective calls

**Title from the first prompt, not the clock.** Counter: prompts are ugly — "hey", pasted code — and rows named after user text read chat app, not instrument. People notice: with prompt names they find a session by remembering the question; with clocks they scan and open candidates.

**The live row stays clickable, dot pulsing, while you read elsewhere.** Counter: greying it is honest about a single mouth and less state to keep consistent. Difference felt: a grey row makes you wait; a pulsing, clickable one reads as a process manager you trust.

**Delete confirms in place, not in a dialog.** Delete turns the row's meta line into "yes / no". Counter: the AlertDialog port is a documented step away, and in-place confirmation risks misfires on 375 targets. Difference felt: a dialog is a modal, a scare; in-place keeps the rail quiet. I'd look at a seeded 375 screenshot mid-confirm before committing.

### 3. The one thing you would cut

Delete itself. It is the only affordance whose absence nobody notices — single-user, local, disk is cheap — yet it drags in everything loud: a permanent destructive control, confirmation design, the just-deleted state, and one-way loss in an app whose promise is persistence. Put it back when the rail grows a context-menu shape and undo worth its cost.
