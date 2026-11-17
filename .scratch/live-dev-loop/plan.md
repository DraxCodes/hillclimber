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

- [x] **1.1** `DevStamp` (`ClimberWeb/Services/DevLoop.cs`) + `GET /__dev/stamp` mapped only when
  `IsDevelopment()`. JSON `{ build, source, startedAt, pid }`: the loaded assembly's write time, the
  newest watched source write (`Components`, `Services`, `wwwroot`; skips `bin`/`obj`/`lib`/
  `node_modules`), the process start time and the PID. No path or version leak.
  *Tests:* the Development gate; observed 200 with the expected payload; process id and assembly
  stamp present; newest watched write wins; foreign extensions and generated/vendored directories
  are ignored; a missing tree has no stamp; the serialized payload contains no path.
- [x] **1.2** `wwwroot/js/dev-refresh.js`: poll `/__dev/stamp` every 1 s (plus one immediate tick),
  reload once per change after a 1.2 s settle, hold while an `input`/`textarea`/`select` is focused
  or a dialog is open, ignore non-200 and failed fetches (a restarting host), and remember the last
  stamp in `sessionStorage` so a save right after a reload is not missed.
  *Verified live:* idle → reload in 2.9 s; focused in the PIN field → held for 5 s; blur → reload in
  1.0 s. The script tag renders only in Development, behind `DevLoop.IsEnabled`, pinned by a test.
  *Floor note:* no new Tailwind class, so no `npm run css`.

## Lane 2 — the host script

- [x] **2.1** `scripts/dev/host.mjs`: `start | stop | restart | status`. Runs
  `dotnet watch --project ClimberWeb --non-interactive --property:BaseOutputPath=<tmp>/dev/bin/`
  with `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5179`, and
  `--no-launch-profile` (D2). Logs to a pinned temp file, waits for 200 on 5179, and refuses to
  adopt a foreign listener unless `--force`.
  *Verified live:* start is idempotent; stop frees the port and reports down; restart comes back in
  ~15 s; `status` reports the stamp, the watch pid, the app pid and render exceptions in the log.
  *Fixed on the way:* the first `--force` killed its own caller — `netstat` matching also counts
  established client connections, so the owner list is now a listener-only query.
- [x] **2.2** README "Working on the UI live": the two traps (hot reload is not trusted; the
  intermediate output must not move) and the three commands.

## Lane 3 — the driver

- [x] **3.1** `scripts/dev/drive.mjs`: loopback control server over one persistent Playwright
  context. Commands: `goto <route>`, `text`, `errors`, `shot <path>`, `reload`, `stop`. Auto-fills
  the PIN when `#pin` is present (D1); headed by default, `--headless` for scripted runs; token in
  a temp file, never in the repo.
  *Verified live:* one headed window, unlocked, parked on `/`; `goto` reaches `/`, `/activity` and
  `/status` and unlocks each; `errors` empty on all three; `shot` writes a PNG; a save reloaded the
  window (loads 1 → 2) and it stayed unlocked.
  *Fixed on the way:* `/` renders its own gate button, so the driver clicks by accessible name
  rather than a CSS class; the gate resolves after the first render, so `goto` waits for the
  decision instead of a fixed delay.
- [x] **3.2** Parked: one long-lived headed driver on `/` for the session.

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