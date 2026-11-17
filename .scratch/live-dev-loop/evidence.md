# Evidence — live dev loop

**Date:** 2026-11-17. **Spec:** [spec.md](spec.md). **Plan:** [plan.md](plan.md).
**Target:** 8. **Surface:** the loop's own commands and the tab it keeps alive.

## Seed

None. This lane does not exercise transcript or session state, so there is no sample data and
nothing to remove. The state used is the app's own: three routes (`/`, `/activity`, `/status`), a
restart, and a temporary one-line copy edit on `ActivityPage.razor` that was reverted before the
commit.

## Shots

All under `%TEMP%\climb-ui-shots\`.

| Shot | What it shows |
| --- | --- |
| `loop/activity-900-light.png` | `/activity` at 900, light, after the loop has reloaded it |
| `loop/activity-900-dark.png` | same, dark |
| `loop/activity-375-light.png` | same, 375 |
| `loop/activity-375-dark.png` | same, 375, dark |
| `loop-before.png`, `loop-after.png` | the probe edit: `Read-only.` → `Read-only · loop probe.` |
| `driver-home-headed.png`, `driver-status-headed.png` | the persistent headed window on `/` and `/status` |

The app's own screens are unchanged by this lane; the 900/375 × light/dark pass is there to show
that the added script tag and endpoint did not alter them.

## Read-through

**First run, from the user's seat.**

1. `node scripts/dev/host.mjs start` → one line naming the URL, the watch pid and the log. Nothing
   opens; the app is at 5179.
2. `node scripts/dev/drive.mjs start` → one window, already unlocked, parked on `/`. `status` names
   the URL, title, unlock state, console-error count, load count and pid. No guess needed.
3. A save → the window reloads itself. Measured **2.9 s** idle; **held** while the PIN field had
   focus; **1.0 s** after focus left.
4. `drive.mjs goto /activity` → unlocked and readable; `text` returns the page; `shot` writes a PNG.
5. `drive.mjs stop` → "stopped"; `host.mjs stop` → port free. No command dead-ends: every failure
   names the next action ("the driver is not running. Start it with: …").

**Keyboard pass.** The loop adds no focusable UI. The only new bytes in the page are a script tag
and the Development-only refresh loop; the keyboard path is the app's own and is covered by
`theme-nav` (focus visible at every stop) unchanged.

**Pauses.** One: after a restart the tab asks for the PIN again, because the unlock lives in the
server's memory and a restart clears it. That is the app's existing behaviour, unchanged by this
lane, and it is the named edge below.

## Flaws found and fixed during the lane

1. **A reload could land in a restart window** — `/activity`, after reverting the probe edit. The
   tab reloaded 1.2 s after the save, the host restarted to apply it, and the page landed on
   `ERR_CONNECTION_REFUSED` for `_blazor/negotiate`: 262 console errors and a locked page. Fixed by
   making the refresh wait for a changed pid (a restart that has already answered), and by giving a
   source-only change a 3 s quiet period. Re-verified after the fix: no errors, still unlocked.
2. **The refresh baseline reset on its own reload** — a save within the first second after a reload
   was missed. Fixed by remembering the stamp in `sessionStorage`.
3. **`host.mjs --force` killed its own caller** — the owner list came from `netstat`, which also
   matches established client connections, so the script saw itself. Fixed with a listener-only
   query.
4. **The driver clicked a class that only two of three pages render** — `/` has its own gate button.
   Fixed by clicking by accessible name.
5. **`goto` reported "locked" while it was unlocking** — the gate resolves after the first render.
   Fixed by waiting for the decision instead of a fixed delay.

## Flaws and strengths

| Where | What | Condition |
| --- | --- | --- |
| Strong: `drive.mjs start` | one window, already unlocked, parked on the route under work | first run |
| Strong: `host.mjs status` | names what is running (pid, build, source, render errors) instead of assuming | any time |
| Strong: the tab | reloads itself on a save and holds while typing | 900 and 375, both themes |
| Edge: after a restart | the tab shows the PIN form and needs one re-entry | only when a change cannot hot-apply |
| Edge: `host.mjs status` | `build`/`source` are ISO stamps with no gloss | a reader who has not read the spec |
| Limit: watch | also notices the repo `bin`/`obj` writes of a `dotnet test` and logs "No managed code changes" | running tests while the host is up |
| Limit: watch | dropped a `Program.cs` change in an earlier probe, and did not pick up a new `.razor` file in one test | rare; `host.mjs restart` is the recovery |

## Contract

No UI surface changed: no new component, class, colour or token. The only additions are a
Development-only `<script>` tag in `App.razor` and a Development-only endpoint. `docs/ui-contract.md`
needs no entry.

## Floor

| Command | Result |
| --- | --- |
| `dotnet build HillClimberUI.slnx --nologo` | 0 warnings, 0 errors |
| `dotnet test --nologo -v minimal` | **Passed 722**, Failed 0, Skipped 0 — run while the app was live |
| `node scripts/dev/host.mjs status` after the tests | still up, 200 |
| `node scripts/ui/theme-nav.mjs` | crawled 3 pages, FAILURES: 0 |
| `node scripts/ui/security.mjs` | FAILURES: 0 (unchanged; the PIN gate is real) |
| `node scripts/ui/providers.mjs` | 15 checks, 0 failures |
| `node scripts/ui/polish.mjs` | not run (supporting evidence only) |
| `node scripts/ui/surfaces.mjs` | not run (needs approval) |

## Scorecard

```text
Score: 8/10 — Clean & Clear
Target: 8
Evidence: no seed (no transcript state); shots loop/*.png, loop-before/after.png,
  driver-*-headed.png; read-through: two commands to a live unlocked window, one pause
  (PIN after a restart)
Flaws holding it at 8:
  1. A restart re-locks the tab (one PIN re-entry) — after any non-hot-patchable change.
  2. `build`/`source` are unglossed ISO stamps — `host.mjs status`, for a reader without the spec.
Why not 9: the PIN re-entry is a visible pause on the watch path, not a nit.
Why not 7: every command completes without a guess; failures name the next action; no dead ends;
  the app stayed up for the whole floor, including a full `dotnet test` run.
Contract: no violations (no UI surface changed)
Floor: build 0/0; dotnet test 722 passed / 0 failed; theme-nav 0; security 0; providers 15/15;
  polish not run; surfaces not run
```