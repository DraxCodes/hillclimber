# Spec: Real-project readiness (fixture climb, UI proof, resilience, spend)

## Context

The dashboard drives a hill-climb loop (`opencode run` → `dotnet test`) and exposes a
`/status` page, but nothing proves the loop works against a real target, every
interactive surface has only ever been checked ad-hoc from an external harness, some
failure modes are undefined, and there is no view of OpenRouter connectivity or spend.

This spec makes the app ready for a *real* project: a tiny committed fixture the climb
loop can demonstrably improve, a repeatable browser check of every UI surface, defined
graceful behaviour for the common failure modes, and connection + spend surfaces fed by
OpenRouter (where a key is configured) and local run costs.

## Decisions (locked with the user before specing)

1. **One spec, phased commits.** This document; implemented as fixture+proof → UI checks
   → resilience → connection+spend. One `/code-review e23f7e3` at the end.
2. **Fixture — committed source, temp-generated repo.** The fixture's *source* lives
   under `fixtures/simple-climb/`; a script copies it to a temp directory, `git init`s and
   commits it, and prints the path. This repo's history is never polluted and the climb's
   `git checkout -- .` operates on the throwaway copy.
3. **Spend source — hybrid + Management key.** `GET /api/v1/key` (normal key) gives
   connection status and key usage; `GET /api/v1/credits` + `GET /api/v1/activity`
   (Management key) give account-total spend and 30-day activity. Per-hillclimb spend is
   summed locally from `step_finish` costs. Missing key / missing management key /
   network failure all degrade to an explicit "not configured"/"unavailable" state.
4. **API key — env → gitignored local json.** `Climber:OpenRouterApiKey` and
   `Climber:OpenRouterManagementApiKey` are read from configuration, which means env vars
   (`Climber__OpenRouterApiKey`, `OPENROUTER_API_KEY`) or a gitignored
   `appsettings.local.json`. Never committed; the UI says "not configured" when absent.
5. **"Saved with skills" — not invented.** There is no honest counterfactual (what the
   same work would have cost without the skills), so the activity page states it is not
   measured and why, rather than fabricating a number. Matches how `/status` handled
   "model per change".
6. **Layout — `/` essentials + new `/activity`.** `/` gains a compact connection +
   spend line (connection dot, total spent overall, this climb's spend); `/activity` gets
   the 30-day graph, per-model breakdown, key usage/limit, per-hillclimb detail, and the
   "saved with skills" state — reusing the `/status` chrome and tokens.
7. **`/hillclimb` only for the fixture climb.** The app changes are built with the
   everyday spec → implement → `/code-review` loop; the *app's own* climb loop is exercised
   against the fixture to prove keep/revert/stop end to end.

## Goal

Prove the climb loop and every UI surface, make the common failure modes graceful and
explicit, and surface OpenRouter connectivity and spend without ever inventing a number
or committing a secret.

## What it should do

### A. Fixture climb target

1. `fixtures/simple-climb/` is a tiny, self-contained .NET test project (a `Calculator`
   with a couple of unimplemented members and a handful of xunit tests, some failing) plus
   a short README stating its purpose.
2. `scripts/climb-fixture.ps1` copies the fixture into a temp directory, `git init`s it,
   commits the initial state, and prints the absolute path — so a climb can run against it
   and revert changes without touching this repo.
3. A documented, one-line goal prompt ("Implement the Calculator methods so all tests
   pass") converges in a couple of iterations.
4. A real climb against the fixture is run through the dashboard and its outcome
   (iterations, keep/revert, best score, stop) recorded in this spec's evidence section.

### B. Repeatable UI checks

5. `scripts/ui/surfaces.mjs` (following `theme-nav.mjs`) drives a running server and
   asserts every interactive surface: PIN gate (wrong + correct), folder picker
   open/navigate/cancel/choose, goal + max-iteration inputs, start validation errors,
   start/stop/lock, theme toggle, nav to `/status` and `/activity`, the live output panel,
   and the new spend surfaces — with zero console errors and a screenshot per page.

### C. Resilience

6. A missing executable (`opencode`, `git`, `dotnet`) is not an exception: the command
   runner reports exit code 127 with a clear stderr message, and the loop logs
   `[<tool> not found — is it on PATH?]` and continues/ends gracefully.
7. A pre-flight probe (pure, unit-tested) reports issues for the chosen target — folder
   absent, not a directory, not a git repository, or a required tool missing from `PATH` —
   surfaced as non-blocking warnings in the configure panel; the run is not silently
   started against an impossible target.
8. A missing/unreachable OpenRouter key, a network failure, and a non-JSON/unexpected API
   response all degrade to an explicit status, never a thrown exception or a blank page.

### D. Connection + spend

9. An `IOpenRouterClient` reads `GET /api/v1/key` (connection + key usage/limit),
   `GET /api/v1/credits` (account total: purchased vs used) and `GET /api/v1/activity`
   (30-day, per-day/per-model usage). Parsing is pure and unit-tested; transport failures
   return a typed "unavailable" result.
10. The climb accumulates `step_finish` `part.cost` (and tokens) for the run and exposes
    it on `ClimberState`, so "this climb's spend" is exact and needs no persistence.
11. `/` shows a compact connection + spend line: a connected/not dot, total spent overall
    (account), and this climb's spend. Nothing else moves to `/`.
12. `/activity` (linked from the nav beside `status`) shows: connection detail (key label,
    free tier, usage, limit/remaining), account total, the 30-day activity graph, a
    per-model breakdown, this climb's spend, and the "saved with skills — not measured"
    state with its one-line justification.
13. Configuration names and the gitignore keep keys out of the repo; the repo ships with
    no key and the pages still render.

## Out of scope

- Persisting climb-run history across restarts (per-climb spend is in-memory, matching
  the loop's existing "runs are not persisted" stance).
- Any charting dependency or client-side JS framework.
- Actually spending money in tests: OpenRouter calls are unit-tested against canned JSON;
  the browser checks run against whatever key is (or isn't) configured locally.
- Backfilling historical spend the app did not perform (account totals come from the API).
- Auth changes: `/activity` is read-only like `/status` and needs no PIN.

## How we know it is done

- `dotnet test --nologo` stays green (109 passing today) plus new unit tests for: the
  fixture existence/shape, command-runner missing-executable handling, the pre-flight
  probe, OpenRouter response parsing (key/credits/activity, present and malformed), and
  `step_finish` spend accumulation.
- `scripts/climb-fixture.ps1` materialises a clean git repo; a real climb against it shows
  at least one kept improvement and ends `Completed` (or `Stopped`) without a crash.
- `node scripts/ui/surfaces.mjs` passes with 0 failures and 0 console errors against a
  running server.
- `node scripts/ui/theme-nav.mjs` still passes (the new `/activity` link is crawled
  automatically).

## Evidence

_(filled in during implementation: fixture path, climb result, harness output.)_

## Fixed point for review

Session start HEAD is `e23f7e3` (`docs: record standing working preferences for agents and
handoffs`), which also covers the unreviewed theme fix (`9617c29`, `4eb79fe`).
`/code-review e23f7e3`.
