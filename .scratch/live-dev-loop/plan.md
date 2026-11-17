# Thread plan — live dev loop

**Status:** proposed, not built. **Date:** 2026-11-17.
**Spec:** [spec.md](spec.md).
**Next after this:** the serve producer (`serve-producer`), which replaces `opencode run` as the
run transport so live model questions become answerable.

Each step is one atomic commit and reverts alone. `[free]` = no process; `[live]` = against a real
running app; no step spends tokens. Floor after every step: `dotnet build`, `dotnet test`, and the
free UI harnesses against the live host.

## Lane 0 — spec

- [x] **0.1** `spec.md` written, with the probe evidence and the six decisions.

## Lane 1 — stamp and refresh (product code)

- [x] **1.1** `DevStamp` (`ClimberWeb/Services/DevStamp.cs`) + `GET /__dev/stamp` mapped only when
  `IsDevelopment()`. JSON `{ build, startedAt, pid }` from the loaded assembly's write time, the
  process start time and the PID. Loopback-only, no path or version leak.
  *Tests:* the endpoint maps in Development and not otherwise (decision helper, not a live host);
  payload shape; the build stamp changes when the assembly file changes; no secret or path in the
  payload.
- [x] **1.2** `wwwroot/js/dev-refresh.js`: poll `/__dev/stamp` every 1 s, reload once per stamp
  change, skip while an `input`/`textarea`/`select` has focus or a dialog is open, stop after the
  reload is requested, ignore non-200.
  *Tests:* pure decision function extracted and unit-tested (`shouldReload(prev, next, focus,
  dialog)`); no fetch when the stamp 404s. The script tag is rendered only in Development, behind a
  helper pinned by a test.
  *Floor note:* no new Tailwind class, so no `npm run css`.

## Lane 2 — the host script

- [x] **2.1** `scripts/dev/host.mjs`: `start | stop | restart | status`. Runs
  `dotnet watch --project ClimberWeb --non-interactive --property:BaseOutputPath=<tmp>/dev/bin/`
  with `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5179`, and
  `--no-launch-profile` (D2). Logs to a pinned temp file, waits for 200 on 5179, and refuses to
  adopt a foreign listener unless `--force`.
  *Tests (free, no host):* argument parsing; port-holder detection with a fake listener; the
  command it would run; restart is stop-then-start and exits non-zero when the port never comes up.
  *Live:* start it once, edit a component, confirm the page reflects the change and the PID is
  unchanged; then `dotnet test` with the app up.
- [x] **2.2** Record the two traps in `README` dev notes (or `docs/`): hot reload is not trusted,
  and `BaseIntermediateOutputPath` must not move.

## Lane 3 — the driver

- [x] **3.1** `scripts/dev/drive.mjs`: loopback control server over one persistent Playwright
  context. Commands: `goto <route>`, `text`, `errors`, `shot <path>`, `reload`, `unlock`. Auto-fills
  the PIN when `#pin` is present (D1); headed by default, `--headless` for scripted runs; token in
  a temp file with a 0600-equivalent ACL, never in the repo.
  *Tests (free):* command routing against a fake page; token required; the driver never writes
  outside its temp shot directory; a second instance refuses to start on the same port.
  *Live:* open `/`, unlock, screenshot; navigate to `/activity` and `/status`; confirm `errors` is
  empty on all three.
- [x] **3.2** Park it: one long-lived driver on `/` while the work happens, so the user can watch
  the same window.

## Lane 4 — evidence and gate

- [x] **4.1** Take the screenshots that prove the loop: before/after an edit without a restart, the
  same page after a forced restart, and the tab auto-refreshing. Record in `evidence.md`.
- [x] **4.2** Floor, all against the live host: `dotnet build`, `dotnet test`, `theme-nav`,
  `security`, `providers`. `security.mjs` passing unchanged is the proof of D1.
- [x] **4.3** Gate at **8** on the live-loop experience (`docs/agents/hillclimb-rubric.md`), with
  the user's seat as the judge: app never dark, tab follows, screenshot on demand.

## Lane 5 — deferred to the next pass

- [ ] **5.1** Control endpoint `POST /__dev/questions` (`emit` / `reset`) over the existing
  `IQuestionStimulus` seam, Development + `CLIMBER_DEV_CONTROL=1` + loopback + per-process token.
- [ ] **5.2** MCP sidecar in `scripts/dev/` exposing `emit_question`, `answer`, `read_transcript`,
  `screenshot` over the same control port.
- [ ] **5.3** Optional: the stamp carries a focus route so the user's tab can follow the agent's
  route without a reload. Only if the loop feels incomplete without it.

## Floor (every step)

```powershell
node scripts/dev/host.mjs start          # 5179 stays up for the whole session
dotnet build HillClimberUI.slnx          # against the repo bin, never the dev host's
dotnet test --nologo
cd scripts/ui; node theme-nav.mjs; node security.mjs; node providers.mjs
node scripts/dev/drive.mjs shot C:\Users\joelp\AppData\Local\Temp\climb-ui-shots\loop.png
```

`polish.mjs` is supporting evidence only. `surfaces.mjs` is **not run** without approval. Report
anything not run as "not run".