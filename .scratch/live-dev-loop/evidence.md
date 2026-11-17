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

The 8 that had been self-scored in this file was therefore wrong and is withdrawn. This revision
fixes all four and records the proof below; the re-gate result belongs in plan **4.3**.

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
| `loop-before.png`, `loop-after.png` | the probe edit: `Read-only.` → `Read-only · loop probe.` |
| `driver-home-headed.png`, `driver-status-headed.png` | the persistent headed window on `/` and `/status` |

The shots wait out the page's "Checking providers…" placeholder, so they show the loaded page, not
a spinner. The app's own screens are unchanged by this lane.

## Read-through

**First run, from the user's seat.**

1. `node scripts/dev/host.mjs start` → one line naming the URL, the watch pid and the log. Nothing
   opens; the app is at 5179.
2. `node scripts/dev/drive.mjs start` → one window, already unlocked, parked on the route named by
   `--route`. `status` names the URL, title, unlock state, console-error count, load count and pid.
3. A save → watch applies it (hot patch in **170–850 ms**) and the window reloads itself. The
   refresh waits for a restart's new pid and holds while a field has focus.
4. `drive.mjs goto /activity` → unlocked and readable; `text` returns the page; `shot` writes a PNG
   at any width/height and either theme.
5. A floor build or test while the host runs → nothing happens to the app (see below).
6. `drive.mjs stop` → "stopped"; `host.mjs stop` → port free. Every failure names the next action.

**Keyboard pass.** The loop adds no focusable UI. The only new bytes in the page are a script tag
and the Development-only refresh loop; the keyboard path is the app's own and is covered by
`theme-nav` (focus visible at every stop) unchanged.

**Pauses.** One: after a restart the tab asks for the PIN again, because the unlock lives in the
server's memory and a restart clears it. That is the app's existing behaviour, unchanged by this
lane, and it is the named edge below.

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

## Flaws and strengths

| Where | What | Condition |
| --- | --- | --- |
| Strong: `drive.mjs start` | one window, already unlocked, parked on the route under work | first run |
| Strong: `host.mjs status` | names what is running and refuses to call an unmanaged app "up" | any time |
| Strong: the tab | reloads itself on a save and holds while typing | 900 and 375, both themes |
| Strong: floor | build and test leave the host and the tab untouched | host live |
| Edge: after a restart | the tab shows the PIN form and needs one re-entry | only when a change cannot hot-apply |
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
| `dotnet build HillClimberUI.slnx --nologo` | 0 warnings, 0 errors |
| `dotnet build HillClimberUI.slnx --no-incremental` | 0 warnings, 0 errors; host pid and tab unchanged |
| `dotnet test --nologo -v minimal` | **Passed 722**, Failed 0, Skipped 0 — run while the app was live; 0 bytes added to the watch log |
| `node scripts/dev/host.mjs status` after the tests | still up, watch + app pids unchanged, exit 0 |
| `node scripts/dev/drive.mjs status` / `errors` after the tests | same pid, unlocked, 0 console errors |
| `node scripts/ui/theme-nav.mjs` | crawled 3 pages, FAILURES: 0 |
| `node scripts/ui/security.mjs` | FAILURES: 0 (unchanged; the PIN gate is real) |
| `node scripts/ui/providers.mjs` | 15 checks, 0 failures |
| `node scripts/ui/polish.mjs` | not run (supporting evidence only) |
| `node scripts/ui/surfaces.mjs` | not run (needs approval) |

## Scorecard

Not self-scored. The previous independent gate scored **6/10 (`REVERT`)** and named four flaws; all
four are fixed and verified above. The re-gate result belongs in plan 4.3.