# Spec: Hill-climb loop

## Context

ClimberWeb drives an automated "hill climbing" loop: it repeatedly asks an agent
(`opencode run`) to improve a target repository, runs the test suite, and keeps
only the changes that increase the number of passing tests. This spec covers that
loop and the small pieces it needs: running external commands and reading the
pass/fail counts out of a `dotnet test` run.

## Goal

Given a target folder, a goal prompt, and a maximum number of iterations, run up
to N iterations. After each iteration, measure the passing tests and keep only the
changes that improve the best score seen so far.

## What it should do

1. Starting a run requires a target folder and a goal prompt; blank or missing
   values are rejected.
2. The maximum iteration count is treated as at least 1.
3. Only one climb may run at a time. Starting another while one is running is an
   error, not a silent restart.
4. Each iteration runs, in the target folder:
   `opencode run "<goal>" --format json`, then `dotnet test --nologo`.
5. The pass/fail counts are parsed from the `dotnet test` summary line. If no
   recognizable summary is present, the iteration counts as 0 passing.
6. If this iteration's passing count beats the best so far, keep the changes and
   update the best score.
7. If it is worse than the best, revert the working tree (`git checkout -- .`) so
   the repository returns to the last good state.
8. If it is equal to the best, keep the changes.
9. A non-zero exit code from `opencode run`, `dotnet test`, or `git` is logged but
   does not abort the run.
10. Stopping a run cancels it promptly, and the run ends in a "stopped" state.
11. An unexpected exception ends the run in a "failed" state, recording the message.
12. The UI can watch progress through an immutable state snapshot plus a change
    event, and a live log feed that is bounded so it cannot grow without limit.

## Out of scope

- Choosing the goal prompt or the target folder; the caller supplies them.
- Committing changes to git. The loop only reverts; it never commits.
- Running two climbs at once.

## How we know it is done

The service's unit tests cover the keep / revert / keep-on-equal branches, non-zero
exit codes, cancellation, unparseable output, and the empty-session guards.
