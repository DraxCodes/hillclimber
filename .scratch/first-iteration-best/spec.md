# Spec: First-iteration best score

## Context

The hill-climb loop (see `.scratch/hill-climb-loop/spec.md`) tracks the highest
passing-test count seen so far and keeps only iterations that improve it. In
`HillClimberService.RunLoopAsync` that score is held in a local `best` seeded to
`-1` — a sentinel meaning "no measurement yet" — while the run's initial
`ClimberState.BestScore` is `0`. Because the first iteration always satisfies
`best < 0`, any first result is reported as "New best score", including one where
no test summary was parsed and the count was assumed to be 0. That contradicts
the loop spec's item 6 ("beats the best so far") and misreports an unmeasured
first iteration as an improvement.

## Goal

Make the tracked best score consistent with the run's initial state: the first
iteration is reported as a new best only when its passing count exceeds the score
carried into it, and a first iteration that measures 0 keeps its changes without
being called a new best.

## What it should do

1. The best score used by the loop starts at `0`, matching
   `ClimberState.BestScore` at the start of a run — no `-1` sentinel.
2. An iteration whose passing count exceeds the best so far is kept, updates the
   best score, and is logged as a new best (unchanged behavior).
3. An iteration equal to the best so far is kept and logged as unchanged. A
   first iteration that measures 0 therefore falls here, not under a new best.
4. An iteration below the best so far is reverted with `git checkout -- .`
   (unchanged behavior).
5. The first iteration is still kept whether its passing count is 0 or positive:
   there is no earlier measurement to revert to, and item 8 of the loop spec
   keeps equal scores.

## Out of scope

- Measuring a baseline before the first iteration (running the test suite on the
  untouched tree).
- Changing what `BestScore` means or how the dashboard renders it.
- Any change to the command sequence, cancellation, or failure handling.

## How we know it is done

`dotnet test` stays green, including the existing first-iteration and regression
tests, and new unit tests assert that:

- a first iteration that measures 0 is not logged as a new best;
- a first iteration that measures a positive count is logged as a new best and
  sets the best score;
- an equal second iteration is not logged as a new best.
