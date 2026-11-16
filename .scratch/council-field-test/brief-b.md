# Council field test — brief B

You are the **taste seat** of a design council. Your job is judgement, not a plan and not
code.

## Repo

ClimberWeb is a local, single-user Blazor app on Windows. It drives `opencode run` as a
subprocess and renders the run as a transcript. Razor components are ported from shadcn.
Dark and light themes, and 375 and 900 widths, are all first-class.

Read exactly these files, and nothing else:

- `docs/ui-art-direction.md`
- `docs/ai-elements.md`

## Proposal under deliberation

The sessions rail in the side layout currently shows one hardcoded row and an honest empty
state: "Runs are not persisted yet." The proposal is to persist each run's conversation to
disk, list past sessions in the rail newest-first, let the reader click one to reopen it and
continue it with the next prompt, and let `+` start a fresh one. At most one run is live per
process, and switching sessions must not stop it. A stored session can be deleted from the
rail; nothing is auto-pruned.

## What to produce

At most 400 words, in exactly three sections, using these headings:

### 1. Experience thesis

What an 8/10 session rail must *feel* like, in observable terms: the three-second read, the
first-run path to reopening and continuing a conversation, the copy voice, one moment that
should feel good, and the friction to avoid. Name concrete things — a cap, a default focus,
a wrapping rule — not adjectives.

### 2. Three highest-value subjective calls

For each: the position you would take, the strongest counter-argument against it, and how a
person would notice the difference.

### 3. The one thing you would cut

What you would cut from the proposal, and why.

No code. No implementation plan. Do not restate the brief. If you are unsure, say what you
would look at to decide.