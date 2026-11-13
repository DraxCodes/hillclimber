# Spec: General OpenCode workflow on the main page (method switch)

## Context

The dashboard is a single hillclimb-shaped page: `HillClimberService` owns one loop
(goal + target + max iterations → `opencode run` → `dotnet test`), and `/` renders
`ClimberState` (iteration, best score, last pass/fail). But the everyday workflow — the
one used to build this very feature — is a plain OpenCode run: pick a folder, send a
prompt, stream the output, watch spend/connection. The method is not always a hillclimb.

This spec reframes the main page so a **general OpenCode run** is the default method and
the hillclimb loop becomes an optional mode on the same page, keeping the existing design,
tokens, chrome, and the `hillclimber` brand.

## Decisions (locked with the user before specing)

1. **Brand stays `hillclimber`.** Only the main page gains a method switch and a general
   run mode; `<title>`s, the brand mark, the theme, and the `scripts/ui` wiring stay.
2. **One page, method switch (`run` | `hillclimb`).** `/` defaults to `run`; the
   hillclimb-only controls (goal prompt wording, max iterations) appear only in hillclimb
   mode. No `/hillclimb` route.
3. **A new general run service; the loop reuses the shared run plumbing.** A general
   `opencode run` and the climb loop share one opencode-step helper, one bounded log feed,
   one status vocabulary, and one spend/token accumulator. The loop "wraps" the shared step
   and adds measurement/revert; it keeps its own state so the two methods never fight over
   one feed.
4. **Model is display-only.** Show the base model resolved from the target folder's
   `opencode.json` (fallback when absent). No picker, no `--model`.
5. **No `/hillclimb` for this task.** It is a product/identity change with no numeric
   objective: spec → implement → `/code-review 1d47099`.

## Goal

Make `/` a general OpenCode runner by default, with hillclimb as a selectable method,
without changing the visual design or the existing hillclimb behaviour.

## What it should do

### A. Shared run plumbing (extracted, not duplicated)

1. `RunStatus` (Idle / Running / Completed / Stopped / Failed) replaces `ClimberStatus`
   and is shared by both methods.
2. `OpenCodeRun` builds `opencode run <prompt> --format json` and its display line in one
   place, used by both.
3. `RunMetrics` sums `step_finish` cost and tokens from feed lines in one place, used by
   both (the climb maps it into `ClimberState`; the run embeds it in `RunState`).
4. `RunLogFeed` stamps lines `[HH:mm:ss]`, caps the feed at 5000, and snapshots, used by
   both.
5. Existing hillclimb tests stay green: same behaviour, same messages, same bounded feed.

### B. General run service

6. `RunOptions(folder, prompt)`, `RunState` (status, message, logs, spend, tokens,
   started/finished) and `IRunService` (`State`, `StateChanged`, `IsRunning`, `StartAsync`,
   `Stop`).
7. One run at a time; a blank folder or prompt is rejected; cancel → `Stopped`; an
   unexpected exception → `Failed`; exit 0 → `Completed`; a missing `opencode`
   (exit 127) or any non-zero exit → `Failed`, with the reason logged and in `Message`.
8. Spend and tokens accumulate live from `step_finish`, exactly as the climb does; nothing
   is persisted.

### C. Main page

9. A method switch (`run` | `hillclimb`) sits below the status line; `run` is the default.
10. **Run mode:** folder, prompt, read-only model, start / stop / lock; telemetry
    (status · this run's spend · tokens · elapsed); the existing spend line and output
    panel, unchanged. No max-iterations control.
11. **Hillclimb mode:** today's controls and telemetry unchanged (goal prompt, max
    iterations, iteration / best score / last run).
12. **One method at a time:** start is disabled while either method is running and the
    method switch is disabled while either is running.
13. **Method-aware pre-flight:** run needs only `opencode`; hillclimb keeps the
    `opencode` / `dotnet` / `git` tools plus the git-repository check.
14. **Branding generalised where it was method-specific:** the footer
    `engine: opencode + dotnet test` becomes `engine: opencode`; the auth lede no longer
    says "climb loop".
15. `/status` and `/activity` are unchanged. The general run's spend appears on `/` (the
    `/activity` "this climb" section stays climb-specific, honestly labelled).

## Out of scope

- Session resume / message history; the bounded log feed stays the surface.
- Model selection (`--model` picker) or a per-run model choice.
- Persisting runs across restarts.
- Reflecting a general run on `/activity`.
- Auth changes; the PIN gate and the read-only pages are untouched.

## How we know it is done

- `dotnet test --nologo` stays green (135 today) plus new `RunServiceTests` and tests for
  the shared plumbing (`RunMetrics`, `OpenCodeRun`).
- `node scripts/ui/surfaces.mjs` passes with 0 failures and 0 console errors, now also
  checking the method switch, the run-mode controls, and a short run;
  `node scripts/ui/theme-nav.mjs` still passes.
- The reworked main page is verified visually with temporary sample data (a seeded or
  real short run), screenshotted via the Playwright harness, then the seed removed.

## Evidence

- **Tests:** `dotnet test --nologo` → **148 passing** (was 135): 12 new `RunServiceTests`
  cases (streaming, spend, cancel, one-at-a-time, empty input, missing tool, non-zero exit,
  exception, log trim, event sequence) plus 1 method-aware pre-flight case.
- **Shared plumbing:** the loop's tests are unchanged and still pass, and `RunMetrics.Add`
  is covered directly; the loop now maps `RunMetrics` into `ClimberState`.
- **Browser:** `node scripts/ui/surfaces.mjs` → **39 checks, 0 failures, 0 console errors**,
  now covering the method switch (run is default, hillclimb reveals max iterations, prompts
  validate per method) and a real short run that completed
  (`step_finish` `Hello!`, spend `$0.008654`), plus the existing climb start/stop.
  `node scripts/ui/theme-nav.mjs` → theme preserved across `/`, `/activity`, `/status`,
  0 failures.
- **Visual:** `home.png` and `hillclimb.png` screenshots confirm the design is unchanged —
  run mode shows status · this run's spend · tokens · elapsed with a model chip, hillclimb
  mode keeps iteration / best score / last run. Sample data was real short runs, so there
  was no seed to remove.
- **Coverage:** `powershell -File scripts/status.ps1` → 148/148, ~62% line.

## Fixed point for review

Session start HEAD is `1d47099`. `/code-review 1d47099`.
