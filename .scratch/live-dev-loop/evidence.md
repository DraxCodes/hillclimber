# Evidence — live dev loop

**Date:** 2026-11-17. **Spec:** [spec.md](spec.md). **Plan:** [plan.md](plan.md).
**Target:** 8. **Surface:** the loop's own commands and the tab it keeps alive.

## Gate history

The first independent gate scored this lane **6/10 (`REVERT`)** and named four flaws:

1. A plain build while the host was up restarted the app, lost the port race, and left the tab on
   `ERR_CONNECTION_REFUSED` until the next file change.
2. `/status` rendered "Could not locate the repository root" under the dev host.
3. `host.mjs status` printed "up" beside a dead watch pid, with no next action and exit 0.
4. No help text in `drive.mjs`, unglossed build/source stamps, and no way to reproduce the
   900/375 screenshots through the loop itself.

The 8 that had been self-scored in this file was therefore wrong and is withdrawn. All four were
fixed and verified; the re-gate scored **7/10 (`KEEP`)** — floor green, main path clean — and named
three narrower flaws:

5. `drive.mjs start` with the app down printed only "the driver did not answer. Log tail:", with no
   cause: the initial `go()` was uncaught and the child's stderr went nowhere.
6. `shot` validated nothing: `--them dark` produced a raw `500: unsupported mime type "null"`, and
   `--theme drk` / `--width abc` were silently ignored at the previous theme or size.
7. `loop-before.png` was `/` mid-load with no copy line, so the pair did not show the edit; and the
   read-through credited `theme-nav.mjs` with a focus pass it does not perform.

This revision fixes 5–7 and records the proof below. The third gate scored **8/10 (`SHIP`)** at
`c262988`: all three flaws reproduced as fixed, floor green with host and tab untouched, no
required flaw left. Its non-blocking nits and the silent process losses it observed are recorded
under "Next pass" and "One observation" below.

## Seed

None. This lane does not exercise transcript or session state, so there is no sample data and
nothing to remove. The state used is the app's own: three routes (`/`, `/activity`, `/status`), a
restart, and temporary one-line copy edits on `ActivityPage.razor` that were reverted before the
commit.

## Shots

All under `%TEMP%\climb-ui-shots\`, and all produced by the loop's own command — e.g.
`node scripts/dev/drive.mjs shot --width 900 --height 1200 --theme dark <path>` — so every one is
reproducible without a second script.

| Shot | What it shows |
| --- | --- |
| `loop/activity-900-light.png` | `/activity` at 900, light, after the loop reloaded it |
| `loop/activity-900-dark.png` | same, dark |
| `loop/activity-375-light.png` | same, 375 |
| `loop/activity-375-dark.png` | same, 375, dark |
| `loop-before.png`, `loop-after.png` | the probe edit at 900×1200 light on `/activity`: `Read-only.` → `Read-only · loop probe.`, same window and route, taken through `drive.mjs shot` |
| `driver-home-headed.png`, `driver-status-headed.png` | the persistent headed window on `/` and `/status` |

The shots wait out the page's "Reading the repository…" and "Checking providers…" placeholders, so
they show the loaded page, not a spinner. The before/after pair was retaken with the current command
after the re-gate found the old `loop-before.png` was `/` mid-load: the before frame now shows the
pre-edit copy (wrapped as `Read-\nonly.`) and the after frame the edited copy, and neither is a
placeholder. The app's own screens are unchanged by this lane.

## Read-through

**First run, from the user's seat.**

1. `node scripts/dev/host.mjs start` → one line naming the URL, the watch pid and the log. Nothing
   opens; the app is at 5179.
2. `node scripts/dev/drive.mjs start` → one window, already unlocked, parked on the route named by
   `--route`. `status` names the URL, title, unlock state, console-error count, load count and pid.
   Run out of order — `start` before the host — it says `http://localhost:5179 is not answering, so
   there is nothing to open.` and `next: node scripts/dev/host.mjs start`, exits 1, and opens no
   window.
3. A save → watch applies it (hot patch in **170–850 ms**) and the window reloads itself. The
   refresh waits for a restart's new pid and holds while a field has focus.
4. `drive.mjs goto /activity` → unlocked and readable; `text` returns the page; `shot` writes a PNG
   at any width/height and either theme.
5. A floor build or test while the host runs → nothing happens to the app (see below).
6. `drive.mjs stop` → "stopped"; `host.mjs stop` → port free. Every failure names the next action.

**Keyboard pass.** This lane adds no focusable element: the only new bytes in the page are a
Development-only `<script>` tag and the Development-only refresh loop, and the copy edit used for
the before/after pair is reverted. So the keyboard path is exactly the app's own. `theme-nav.mjs`
asserts nothing about focus — it crawls the three routes and checks navigation and theme — and is
**not** cited here as a focus pass. No new focus stop, ordering or visible-focus behaviour is
introduced, so there is nothing new to tab through.

**Pauses.** One: after a restart the tab asks for the PIN again, because the unlock lives in the
server's memory and a restart clears it. That is the app's existing behaviour, unchanged by this
lane, and it is the named edge below.

**One observation, not scored — the host is sometimes killed from outside.** Across the second and
third gates the watch and/or driver disappeared with no log line five times: once around 05:59, at
06:19:56 (the watch already dead while the app still served, the app gone at 06:20:17), before
06:32:5x, 06:33:5x, and 06:36–06:38 (the driver, twice, with no `browser closed` line). What was
ruled out: the Windows Application log has **no process-fault entry** — its only event in the window
is the known Kestrel `BrowserToolsForwarder` WebSocket disconnect, which the app logs as `fail:` and
survives — and the System log has no error; there was 12 GB free, so not memory pressure. What was
ruled in: the same detached trees have repeatedly outlived their launching shells (both parents
gone, watch and driver still serving), so `host.mjs`'s detach works on this machine; and every
`killTree` call in the scripts is behind an explicit `stop`, `start --force`, a watch that answers
nothing, or a start that timed out — none of which ran in those windows. The likely cause is an
external job-object or process-tree cleanup, which the loop cannot prevent. It detects it honestly
(`host.mjs status` exits 1 and names `restart`; `drive.mjs status` names a restart) and the target
"app never goes dark" is therefore only observed for the stretches between those kills, not for a
whole sitting. The loop is per-session tooling, not a service, and this evidence makes no claim that
it survives an external teardown.

## Fixed for the gate

**1. A build must not restart or darken the host.** Two causes, both in `ClimberWeb.csproj`:

- Moving `BaseOutputPath` to the temp directory took the repo `bin` out of the SDK's
  `DefaultItemExcludes`, so watch treated the writes of a plain build as source changes. The repo
  `bin` is now excluded explicitly, whichever output path is in use.
- A design-time build lists the generated `obj/.../ClimberWeb.AssemblyInfo.cs` as a `Compile` item,
  and watch accepts a change to *any* `Compile` item before it consults the excludes. Every build
  rewrites that file, so every build restarted the host. A design-time-only target drops exactly
  that file from the item list. `GlobalUsings.g.cs` must stay — hot reload compiles against this
  item list and loses implicit usings without it (found by testing, not assumed).

*Verified with the host live:* `dotnet build HillClimberUI.slnx --no-incremental` (0 warnings,
0 errors) and `dotnet test` (**722 passed**) left the watch pid and app pid **unchanged**, wrote
**0 bytes** to the watch log, and the open tab stayed unlocked with **0 console errors**.

While doing this the real cause of the earlier bind failures surfaced: **two `dotnet watch` runs
from before the wrapper** were still alive and fighting over 5179 and the temp output. They are
gone; `host.mjs status` now reports exactly that state instead of calling it up.

**2. `/status` must resolve the repository.** `host.mjs` now passes `Climber__RepoRoot=<repo>`,
which `ProjectStatusService` already honours. Verified live: BRANCH `feat/interactive-questions`,
HEAD `a4ed364`, TREE `dirty`, COMMITS 200, and the changelog list render instead of the warning.

**3. `host.mjs status` must not contradict itself.** It now exits non-zero and names the next
command when the watch pid is dead, when the port is held by something that answers nothing, and
when the log's current run ends in a bind failure (the failure line is printed). Verified by running
the app alone with a state file pointing at a dead pid: `up`, `watch: pid 999999 — not running`,
`note: nothing is watching for changes; edits will not be applied`,
`next: node scripts/dev/host.mjs restart`, exit **1**. `build`/`source`/`started` are glossed as a
local clock time plus an age.

**4. The CLI must explain itself.** `help` / `--help` / `-h` on both scripts, and a typo prints the
same help and exits 2. `drive.mjs shot` takes `--width`, `--height` and `--theme light|dark`, and
waits out the page's loading placeholder.

**Second gate — 5. `start` with no host must say why.** `start` now probes the base before it
spawns anything and prints `<base> is not answering, so there is nothing to open.` /
`next: node scripts/dev/host.mjs start`. As a backstop, `serve` catches the initial `go()`, writes
`initial route failed: <Playwright's first line>` to `drive.log`, prints the same line with the next
action, and exits 1; `start` watches for the child's exit instead of polling for the full 30 s and
surfaces that log line. *Verified* with a scratch `TEMP` and `CLIMBER_BASE=http://localhost:5999`:
exit **1**, the two lines above, and **0** browsers launched; invoking `__serve` directly shows
`page.goto: net::ERR_CONNECTION_REFUSED` instead of discarding it.

**Second gate — 6. `shot` must reject, not guess.** The command parses strictly: an unknown `--*`
is refused with the nearest flag (`unknown option "--them" (did you mean --theme?)`), a bad
`--theme` names the two values, a non-numeric or out-of-range `--width`/`--height` names the range
(200–4000), a missing value and a second path are each refused, and every case exits **2** with a
one-line `next:` usage. The control server validates the same fields and answers **400** with a
sentence — verified directly against `/shot`: `path must end in .png, .jpg or .jpeg`,
`theme must be light or dark`, `width must be a whole number from 200 to 4000` — so the old
`500: unsupported mime type "null"` is unreachable, and the driver stayed live (200 on `/status`)
after each rejected request.

**Second gate — 7. The proof must be the proof.** `loop-before.png` and `loop-after.png` were
retaken with the current command, on `/activity` at 900×1200 light, before and after a one-line copy
edit applied by hot reload — the before frame shows `Read-only.`, the after frame
`Read-only · loop probe.`, same window, same route, no placeholder. The read-through no longer
credits `theme-nav.mjs` with a focus pass; it states that the lane adds no focusable element.

## Flaws and strengths

| Where | What | Condition |
| --- | --- | --- |
| Strong: `drive.mjs start` | one window, already unlocked, parked on the route under work | first run |
| Strong: a wrong-order start | names the base, the cause and the next command; opens nothing | host down |
| Strong: a bad `shot` flag | refused with the nearest flag or the valid values, exit 2, no PNG | any typo |
| Strong: `host.mjs status` | names what is running and refuses to call an unmanaged app "up" | any time |
| Strong: the tab | reloads itself on a save and holds while typing | 900 and 375, both themes |
| Strong: floor | build and test leave the host and the tab untouched | host live |
| Edge: after a restart | the tab shows the PIN form and needs one re-entry | only when a change cannot hot-apply |
| Edge: external teardown | watch and/or driver vanished with no log line (five times across two gates), unreproduced by any loop command; recovery is `restart` | outside the loop's own commands |
| Limit: watch | a *forced* full rebuild overflows the OS watcher: "Too many changes at once"; it recovers and the next save applies (verified), but a save during the burst can be missed | `dotnet build --no-incremental` while the host is up |
| Limit: watch | dropped a `Program.cs` change in an earlier probe, and did not pick up a new `.razor` file in one test | rare; `host.mjs restart` is the recovery |
| Limit: a rebuild | a save that cannot hot-patch still costs a few seconds; the tab waits for the new pid instead of reloading into the gap | any structural change |

## Contract

No UI surface changed: no new component, class, colour or token. The only additions are a
Development-only `<script>` tag in `App.razor` and a Development-only endpoint. `docs/ui-contract.md`
needs no entry.

## Floor

| Command | Result |
| --- | --- |
| `dotnet build HillClimberUI.slnx --nologo` | 0 warnings, 0 errors; watch 32856 / app 23232 unchanged; **0 bytes** added to the watch log |
| `dotnet build HillClimberUI.slnx --no-incremental` | 0 warnings, 0 errors; host pid and tab unchanged (earlier run) |
| `dotnet test --nologo -v minimal` | **Passed 722**, Failed 0, Skipped 0 — run while the app was live; host still 32856 / 23232, 0 bytes added |
| `node scripts/dev/host.mjs status` after the tests | still up, watch + app pids unchanged, exit 0 |
| `node scripts/dev/drive.mjs status` / `errors` after the tests | pid 1980, unlocked, 0 console errors |
| `node scripts/ui/theme-nav.mjs` | crawled 3 pages (`/`, `/activity`, `/status`), FAILURES: 0 |
| `node scripts/ui/security.mjs` | FAILURES: 0 (unchanged; the PIN gate is real) |
| `node scripts/ui/providers.mjs` | 15 checks, 0 failures |
| `node scripts/ui/polish.mjs` | 100, 0 defects in 560 checks (supporting only) |
| `node scripts/ui/surfaces.mjs` | not run (needs approval) |

The third gate re-ran the same floor on a host it started itself (watch 37040 / app 12760) with the
same result: build 0/0, 722 passed, both pids unchanged, watch log delta **0**, tab unlocked with 0
errors, theme-nav 0, security 0, providers 15/15, polish 100. The pids in the table above are from
the run recorded here; pids change when a host is restarted.

## Next pass — non-blocking nits from the 8/10 gate

1. `drive.mjs text` can print a page's loading copy (`Checking providers…`, `Reading the
   repository…`) right after `reload` or a cold `goto`; `shot` waits those out, `text` does not.
2. `drive.mjs status` with no state file prints "not running" and exits 1 with no `next:` line —
   the one empty state that does not literally name an action.
3. `drive.mjs shot --help` is refused (`unknown option "--help"`) instead of printing help.
4. `scripts/dev/drive.mjs` has a dead SIGINT/SIGTERM block after the keep-alive promise in
   `serve()`; remove it or move it before the promise. Only reachable by running `__serve` in a
   foreground shell.

All four are in tools the serve-producer lane will touch, so they belong there rather than in a
re-gate of this one.

## Scorecard

Not self-scored. The independent gates scored **6/10 (`REVERT`)**, **7/10 (`KEEP`)** and
**8/10 (`SHIP`)** at `c262988`, naming seven flaws that were then fixed and reproduced as fixed.
The 8 is the gate's number; the nits above are explicitly non-blocking.