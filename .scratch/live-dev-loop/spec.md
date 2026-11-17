# Spec — live dev loop

**Status:** proposed. **Date:** 2026-11-17.
**Branch:** `feat/interactive-questions` (continues from `9f70c43`).
**Supersedes:** the ordering of lane 6 in
[`../interactive-questions/plan.md`](../interactive-questions/plan.md) — the control endpoint and
the MCP sidecar land here, after the loop itself.

## Problem

Every visual lane so far has cost the same cycle: stop the host, build, start it, seed, drive
Playwright, read a screenshot blind, then remove the seed. Two of those steps exist only because
the app and the tests share `ClimberWeb/bin/Debug`, and because nothing lets the agent or the user
watch the running app while the code changes.

Measured on 2026-11-17, spare change only, no model calls:

| Probe | Result |
| --- | --- |
| `dotnet watch --non-interactive` headless, app on 5179 | runs; stays up |
| markup edit while it runs | same PID; hot patch reported in 38–805 ms |
| a fresh HTTP request after that edit | **inconsistent**: `MainLayout` showed the change, `AuthGate`'s prerendered branch did not |
| an already-open tab after that edit | **no repaint**, no reload (0 `load` events) |
| `dotnet test` while the app runs, shared `bin` | `MSB3027`: `ClimberWeb.exe` locked by the host |
| `dotnet test` with the host on a split `BaseOutputPath` | **712 passed, 0 failed**, host still 200 |
| hot-patching `MainLayout.razor` (has a `SectionOutlet`) under a live circuit | **broke rendering**: `There is already a subscriber to the content with the given section ID 'context'` on every new circuit |
| moving `BaseIntermediateOutputPath` off the default | fails: duplicate `AssemblyInfo` attributes (the old `obj` is still globbed) |

Two facts follow. First, the app can stay up across tests and edits, with no repo change, by
building the dev host into its own output directory. Second, **hot reload cannot be trusted**: it
reports success while the page is stale or broken, so the loop has to *detect* staleness instead
of assuming freshness.

## Goal

1. The app runs at <http://localhost:5179> for the whole working session. It goes down only when a
   change cannot hot-apply, and then for the few seconds a restart takes.
2. The user's open tab shows the new UI without being asked to refresh — and is never reloaded
   while they are typing in it.
3. The agent can open the app, authenticate, sit on the route under work, read the rendered text
   and the console, and take screenshots — without tokens, seeds, or a restart per look.
4. No weakening of the PIN gate. `scripts/ui/security.mjs` must keep passing unchanged.

## Non-goals

- No serve producer; that is the next spec (`serve-producer`).
- No MCP sidecar in the first pass. The control surface ships as a loopback HTTP endpoint the
  driver already needs; wrapping it in MCP is the step after.
- No change to how the app runs outside Development, and no new production surface.

## Decisions

**D1 — no auth bypass.** The driver types the PIN like a person does. Auto-login is a driver
behaviour, not a server one, so `ClimberAuth` is untouched, a locked app still looks locked, and
`security.mjs` keeps its meaning. A restart clears the in-memory unlock dictionary; the driver
re-enters the PIN when it sees `#pin`.

**D2 — watch never opens a browser.** `launchSettings.json` has `launchBrowser: true`, which fires
once per start (twice during the probe). The dev host runs with `--no-launch-profile` and sets
`ASPNETCORE_ENVIRONMENT` and `ASPNETCORE_URLS` explicitly, so nothing opens unless the driver opens
it.

**D3 — the dev host builds elsewhere.** `--property:BaseOutputPath=<temp>/dev/bin/` frees the repo's
`bin` for `dotnet test`, builds and the UI harnesses while the app keeps running. The intermediate
path stays in the repo: moving it re-globs the old `obj` and breaks the build with duplicate
`AssemblyInfo` attributes.

**D4 — staleness is measured, not assumed.** `/__dev/stamp` reports the loaded assembly's write
time, the process start time and the PID. If the stamp does not match the built output, the loop
restarts; a reported render exception also forces a restart. Hot reload is an optimisation, never
the source of truth.

**D5 — one persistent window.** The driver owns a single persistent Playwright context (headed by
default, `--headless` for scripted runs) parked on the route under work. It is the only browser the
loop opens.

**D6 — refresh defers to the user.** The Development-only refresh script reloads on a stamp change,
but not while an input, textarea or select has focus, and not while a dialog is open. A reload is
never allowed to throw away a half-typed prompt.

## Surface

| Piece | What it is |
| --- | --- |
| `GET /__dev/stamp` | JSON `{ build, startedAt, pid }`. Development only; 404 otherwise. No paths, versions or secrets beyond the build stamp. |
| `wwwroot/js/dev-refresh.js` | Polls the stamp every second; reloads once per change; defers per D6. |
| `Components/App.razor` | Renders the script tag only when Development (decision behind a testable helper). |
| `scripts/dev/host.mjs` | `start` / `stop` / `restart` / `status` for the watch host; refuses to adopt a foreign listener on 5179. |
| `scripts/dev/drive.mjs` | Loopback control server over one persistent context: `goto`, `text`, `errors`, `shot`, `reload`, `unlock`. Token in a temp file, never in the repo. |

## Risks and traps

- **The `SectionOutlet` trap.** Editing `MainLayout` under a live circuit throws and leaves the page
  broken while watch says "applied". The loop must treat any logged render exception as failed and
  restart. Verified once; the plan records it as a rule, not a theory.
- **Stamp vs build race.** A reload triggered mid-build lands on a stale or refused connection. The
  stamp must only advance when the new process is listening, and the refresh must debounce.
- **Auth dictionary reset.** Every restart clears unlocks; the driver re-logs in, the user's tab may
  show the gate again. Acceptable and explicitly not worked around.
- **Moving outputs.** `BaseIntermediateOutputPath` must not move (D3). Only `BaseOutputPath` does.
- **Prod leakage.** `/__dev/stamp` and the refresh script must be absent outside Development, and
  the endpoint must be tested for that, not assumed.

## Floor and target

Floor: `dotnet build`, `dotnet test`, and `theme-nav` / `security` / `providers` run against the
**live** host (proving the loop does not need it stopped). `polish.mjs` is supporting evidence;
`surfaces.mjs` is not run without approval.

Target: **8** on the live-loop experience, judged from the user's seat — app never goes dark, the
open tab follows the work, and a screenshot of any route can be produced on demand. Tokens: none
in this lane.