# Hillclimb quality rubric

This is the decision tool for the hillclimb loop: a 1–10 judgement of how the
output of a prompt actually lands with a person. It replaces technical
completeness and statistical metrics as the iterate/stop signal. Tests and build
stay as a floor (see [The gate](#the-gate)).

Scores are whole numbers. Every level is defined by things you can observe, not by
taste adjectives. The user-supplied anchors at 5, 8 and 10 are restated exactly;
the levels between them fill in the observable traits.

## How to look

Run these probes before scoring. Each is an action, not an impression.

1. **Three-second test.** Show the artifact for three seconds, look away, then name
   the primary action and the current state. If either is not obvious, that is a
   hierarchy flaw.
2. **First-run test.** Someone with no context completes the primary task. Count
   every pause, guess and question. Each one is a friction flaw.
3. **State sweep.** Visit empty, loading, success, error, long-content and
   disconnected states. Also try zero items, one item, the longest label and the
   largest value. A missing, unstyled or misleading state is a flaw.
4. **Width and theme.** Check 375px and 900px wide, in light and dark. Look for
   overflow, clipping, unreadable contrast, and elements that vanish or reorder in
   a way that changes meaning.
5. **Keyboard pass.** Tab from the top. Focus must be visible at every stop, the
   order must match reading order, nothing traps focus, and Escape must close what
   it opened.
6. **Copy read.** Read every visible string aloud. Flag placeholder or debug text,
   internal jargon, duplicated labels, sentences that explain the obvious, and
   errors that blame the user.
7. **Dead-end sweep.** Click every control that looks interactive. It must respond;
   the response must lead somewhere or say why it cannot; a disabled control must
   explain itself.

## Levels

### 10 — Shipped Pro Tool

> Feels like a commercial-grade product crafted by a senior designer or editor.
> High taste, effortless flow, zero friction, anticipates user needs before they
> arise.

Observable traits:

- All seven probes pass, and the reviewer can say "no flaw found" without hedging.
- Every state — empty, loading, success, error, long content, disconnected — is
  designed and readable, with a sensible next action.
- Defaults and preserved context mean the common task needs no setup; the obvious
  next step is already offered.
- No control is a dead end; recovery from an error is one visible action.
- Copy is specific and short: no filler, no repeated labels, no instructions for
  something the interface already makes obvious.
- Spacing, type, alignment and motion are consistent across widths and themes; the
  same role looks the same everywhere.

### 9 — Finished, with one named edge

- The main path and every primary state meet the level-10 traits.
- Exactly one named, low-exposure imperfection remains: a rare state, an advanced
  control, or a single microinconsistency a first-time user would not meet while
  doing the primary task.
- If the reviewer cannot name the one edge, score 10 and state that no flaw was
  found across the probes; if the edge sits on the main path, it is a 7 or lower.

### 8 — Clean & Clear

> Ready for external eyes. Nothing trips up a first-time user, no rough edges,
> nothing looks unfinished or "AI-generated."

Observable traits:

- A first-time user completes the primary task with no questions and no guesses; at
  most one pause, and it resolves itself.
- Every interactive element has visible hover, focus, disabled and busy states.
- Empty, loading and error states exist, are readable, and offer a next action.
- No placeholder, debug, lorem or "coming soon" copy on a shipped surface.
- Spacing, type and alignment are consistent between sibling elements; nothing
  overflows or clips at 375px or 900px in either theme.
- Remaining flaws are nits a user would not notice or name.

### 7 — Nearly clean

- The main path works and looks intentional, but one named rough edge remains on it
  or on a standard state: an unstyled state, a label that needs a second look, or a
  control whose hierarchy disagrees with its siblings.
- A first-time user completes the task but pauses once, and the pause is caused by
  the interface rather than by the task itself.
- Everything else meets the level-8 traits.

### 6 — Usable with visible seams

- Functionally complete, and the task is completed without instructions.
- Repeated small friction or visible inconsistency across states, widths or themes:
  mismatched spacing between sibling panels, two different styles for the same
  button role, copy that explains the obvious, one state that looks unfinished.
- A user would notice the seams but is not blocked by them.

### 5 — Usable but Clumsy

> Functionally works, but feels clunky, verbose, awkward, or makes the user do
> mental heavy lifting to get value.

Observable traits:

- Every function is present and correct.
- The flow is longer than it needs to be: extra steps, a modal where inline would
  do, or fields ordered so the user has to backtrack.
- Labels need interpretation or prior knowledge — jargon, internal names,
  ambiguous verbs.
- Feedback is missing after an action: no busy, saved, failed or empty
  confirmation, so the user has to infer what happened.
- At least one standard state is missing or unpolished.
- A user can get value, but must do mental work the interface should have done.

### 4 — Works after guessing

- The primary task can be completed, but only after a guess or a retry.
- The main action is not the most prominent thing on screen, or its label does not
  match its effect.
- One core state — empty, loading or error — is missing or actively misleading.
- Styling is visibly inconsistent at a glance: mismatched alignment, competing
  emphases, controls that look alike but behave differently.
- A user needs help or a second attempt to succeed.

### 3 — Looks unfinished

- Placeholder, debug, sampled or contradictory content appears on a normal path.
- Several controls lack hover, focus or disabled states, or focus is invisible.
- The layout breaks at a common width (clipping, overlap, horizontal scroll), or
  one theme is unreadable.
- A first-time user needs written instructions before they can start.
- The artifact reads as a mock-up rather than a product.

### 2 — Mostly broken

- A primary path dead-ends: a control does nothing, a state never resolves, or an
  action loses the user's work.
- Labels and controls contradict each other or the visible state.
- Empty, loading and error states are absent, so the artifact looks blank or
  frozen.
- Only someone who already knows the intent can use it.

### 1 — Unusable

- The primary task cannot be completed at all.
- Content is placeholder or contradictory, or the screen is visually broken at the
  default size: overlap, clipping, unreadable contrast.
- Interactive elements are missing, unlabelled, or indistinguishable from static
  text.

## The gate

- **Below 8:** name the specific subjective flaws holding it back (for example
  clunky transitions, visual clutter, unclear hierarchy), fix those flaws, and
  iterate.
- **8 or above:** stop and ship, unless the task explicitly demands a 10.
- **8–9 with a 10 demanded:** name what a 10 requires, attempt at most two
  iterations, then stop and report the ceiling if the score does not move.
- **Floor:** a passing build, `dotnet test` and the free assertion harnesses. A
  red floor cannot score 8 and cannot ship.

The score is the only iterate/stop signal. A green floor does not earn a ship; a
number moving does not earn a ship. Only the rubric does. Two consecutive
iterations without a higher score stop the loop.

`scripts/ui/polish.mjs` is supporting evidence, not a threshold: it reports a UI
Polish Score and exits 0 whatever the score, so only a harness error or a detected
run fails it. Do not turn its UPS into a gate until backlog issue 05 adds one.

## Evidence a score must cite

A score without this evidence is invalid and is treated as below 8.

- **Seed:** the temporary sample data used, what state it exercises, and where the
  seed lives. It stays uncommitted, in one throwaway file plus one hook.
- **Shots:** screenshots at 900 and 375, light and dark, named by screen and state.
- **Read-through:** a short note from the first-run and keyboard passes — what was
  tried, what paused, what was guessed.
- **Flaws and strengths:** each one states *what* is visible, *where* (screen,
  selector, viewport, theme), and *what condition* exposes it. "Cluttered" alone is
  not evidence; "three equally weighted buttons above the input at 375 light"
  is.
- **Floor:** the exact commands run and their observed result; anything not run is
  labelled "not run".

Scorecard shape:

```text
Score: 7/10 — Nearly clean
Target: 8
Evidence: seed <file>; shots <names>; read-through <note>
Flaws holding it at 7:
  1. <what> — <where> — <condition>
  2. <what> — <where> — <condition>
Why not 8: <the level-8 trait that is missing>
Why not 6: <the level-6 trait that is absent>
Floor: dotnet test <result>; build <result>; polish <UPS and exit, or not run>
```

## Anti-inflation rules

- An 8 or above must name the level-7 flaws that are demonstrably absent.
- A 10 must state that no flaw was found across all seven probes.
- A below-8 score must name at least one concrete flaw with its location and the
  condition that exposes it.
- Scores are whole numbers; no 7.5.
- Record the score per iteration with its delta and the reason. Two consecutive
  iterations without a higher score stop the loop; a drop reverts the change.
- `quality-gatekeeper` sets the score and `task-orchestrator` applies it. The
  orchestrator cannot raise a score; a disagreement is settled by re-reading the
  evidence, not by averaging.

## Applying it beyond screens

The same levels apply when the artifact is not a screen; only the evidence changes.

- **Docs:** render the markdown, then read it as a newcomer. Check the empty and
  error examples, long tables, and the rendered width if it ships as a page.
- **CLI:** run the command with no arguments, bad arguments and long output. Judge
  the help text, error text and exit codes by the copy and dead-end probes.
- **API or backend:** inspect the response shape, the empty and large payloads, and
  the human-facing error or notification. The user is whoever reads the output.
- **Internal-only work with no human-facing surface:** a measurable metric remains
  the target (coverage, latency, size), but the floor and anti-inflation rules still
  apply. If any part is seen by a person, score that part with this rubric.

## Target score

The default target is **8**. A 10 is demanded only when the task prompt or spec
says `Target score: 10` in as many words. When no target is stated, use 8; do not
ask. Record the target before the first score; it does not move during a run.
