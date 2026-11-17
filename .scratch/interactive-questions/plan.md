# Thread plan — interactive model questions

**Status:** lanes 0, 2, 3, 3.5, 4 and 5 done; the live row and its restored states each
scored **8/10** and ship. The live dev loop is **8/10 (`SHIP`)** at `c262988` (see
[`../live-dev-loop/evidence.md`](../live-dev-loop/evidence.md)). Lane 7 (serve producer) is next;
lane 6 (live-app harness) stays deferred. **Date:** 2026-11-17.
**Spec:** [spec.md](spec.md). **Handoff:** [handoff.md](handoff.md).

All of this work lives on the `feat/interactive-questions` branch; `master` is back at
`23be0ba` (= `origin/master`) and has none of it.

Each step is one atomic commit and reverts alone. `[live]` = proved against a real `opencode`
child; `[free]` = no process. Lane order is dependency order. Lanes 4–6 are planned, not built
first.

## Lane 0 — spec

- [x] **0.1** `spec.md` written; link it from `council.md`.

## Lane 2 — transport (`ClimberWeb/Services/`)

- [x] **2.1** `QuestionContract.cs`: kinds, statuses, options, state, outcomes,
  `QuestionLine.Write/Parse`, `QuestionSupport.Classify`. Add `LogKind.Question`,
  `LogEntry.Question`, the `LogFormatter.Parse` branch, the `LogGrouper` case (a question is
  never folded into a turn) and `OutputItemKind.Question`.
  *Tests:* full support matrix; `title` missing → `key`; boolean is JSON `true`/`false`; value
  not label; line JSON round-trip; written line has no `password`/`authorization`/`url` key;
  collapse keeps the last line per form and never reorders; a question closes an open turn.
  *Floor:* build + `dotnet test`.
- [x] **2.2** `OpenCodeServe.cs`: `ServeChild`, `IServeProcess` seam, URL-line resolution,
  per-run password, close-stdin/kill.
  *Tests:* one URL line, loopback only; startup timeout kills the tree; `ToString()` and every
  log string exclude the secret. Guarded `[Trait("Live","true")]` real child binds loopback and
  requires auth; returns early if `opencode` is not on `PATH`.
- [x] **2.3** `OpenCodeFormClient.cs`: list/reply/cancel/get + `WatchAsync` SSE loop and outcome
  mapping.
  *Tests:* status→outcome table (204/409/400/5xx/timeout/refused); frame parser handles `data:`
  with/without space, comments, blank frames. Guarded `[live]` create/reply/409/400/cancel.
- [x] **2.4** `QuestionChannel.cs`: allowlist dispatch, reconcile → `Stale`, reconnect/backoff,
  `Changed`, consent filter.
  *Tests:* `permission.asked` produces no event/state; reconnect lists pending as `Stale`;
  409/400/unconfirmed/disconnected mapping; cancel message never echoed.
- [x] **2.5** Wire into `RunService`/`IRunService` (answer, dismiss, check), publish via
  `Log(QuestionLine.Write(state))`, dispose on terminal, `IAsyncDisposable`.
  *Tests:* fake channel `Pending → Answered` adds exactly two question lines; a throwing channel
  changes no run status; answering with no channel is a no-op `Disconnected`; the password never
  appears in `Logs`.
- [x] **2.6** `Climber:Questions = off | serve` (default `off`) in `QuestionTransport.cs`,
  registered in `Program.cs`.
  *Tests:* `off|serve|garbage → off`.

## Lane 3 — surface (`ClimberWeb/Components/Ui/` + minimal `Home.razor`)

- [x] **3.1** `Ui/Ai/QuestionView.cs` (pure): state → copy, kind label, classes,
  `FirstOptionLabel`, `IsInteractive`.
  *Tests:* every copy string exactly; a 60-char label renders verbatim; no string contains
  `permission`, `allow` or `deny`. (`QuestionViewTests`, 15 cases.)
- [x] **3.2** `Ui/Ai/AiQuestion.razor`: `Trace`/`TraceLine`/`TraceTitle`/`TraceCollapsible`/
  `Button`; `role="radiogroup"` + `radio` rows with roving tabindex, `aria-checked`,
  `aria-busy`; answered disclosure; `dismiss`/`check` as `Button Variant="outline" Size="xs"`
  overridden to a micro mono chip (`border-border`, 11px) so the action does not outrank the
  notice it belongs to.
  Add the surface-map row and a Phase 2i decision block to `docs/ui-contract.md`. *(both written)*
  *Test:* none renderable; the decision block is what the provenance probe reads.
- [x] **3.3** `Home.razor` hook: one `OutputItemKind.Question` branch keyed by form id,
  `ElementReference`s for the options, focus on arrival / back on settle,
  one `role="status"` announcement, `StatusText` override `awaiting answer`.
  *Test:* build; the read-through is the evidence.
  *Fix found by the read-through:* the row's own `OnAfterRenderAsync(firstRender)` ran before the
  option rows reported their `ElementReference`, so arrival focus never moved. The row now focuses
  on the first render pass that has a reference; the announcement was already true.

## Lane 3.5 — dev evidence (replaces the runtime stimulus; `[free]`)

- [x] **3.5.1** Committed replay seed instead of a scenario switch:
  `ClimberWeb.Tests/Fixtures/questions.jsonl`, five row states replayed through the real
  formatter and grouper by `CLIMBER_SEED=questions.jsonl`, pinned by
  `PlaybackFixtureTests.TheQuestionsFixtureReachesEveryRowState`.
- [x] **3.5.2** Throwaway Playwright probes in the temp `climb-ui` folder (focus, keyboard,
  pointer, computed style, aria). No timed arrival hook was needed: a seeded row arrives as data,
  so the focus move could be captured against the real render.
- [ ] **3.5.3** `IQuestionStimulus` (runtime arrival, fake channel, recorded answer) moved to
  lane 6 — it is the groundwork for the live-app lane, not evidence for this row.
- [x] **3.5.4** No new `README.md` row: the seed rides the documented `CLIMBER_SEED`, which is a
  fixture switch that already exists rather than a new one.

## Lane 4 — persistence

- [x] **4.1** `StoredQuestion`/`StoredOption` + `StoredEntry.Question` + `From`/`ToEntry`.
  *Tests:* round-trip through `JsonSessionStore`; a legacy file still loads; no
  `password`/`url` key in the JSON.
- [x] **4.2** Restore rules: pending/`Stale` → `no answer sent`, closed; answered closed;
  every restored row inert with the settled glyph, never the live `asks:`.
  *Tests:* `SessionCoordinatorTests` — pending restores one closed non-interactive row;
  pending+answered restores one `answered:` row.
- [x] **4.3** README row for the persistence shape.

## Lane 5 — evidence

- [x] **5.1** Commit the display seed as `ClimberWeb.Tests/Fixtures/questions.jsonl` (a fixture, not
  an `opencode run` recording), replayed by the existing `CLIMBER_SEED` host. No new `questions.mjs`
  was added: the probes stayed throwaway under `%TEMP%`, and `PlaybackFixtureTests` pins the states.
- [x] **5.2** Read-through and both scorecards recorded in
  `.scratch/interactive-questions/evidence.md`; the gatekeeper scored them 7 → 8.

## Lane 6 — future: live app harness (moved)

**Moved to [`../live-dev-loop/plan.md`](../live-dev-loop/plan.md).** The loop itself (watch host,
stamp, auto-refresh, driver) comes first there; its lane 5 carries the control endpoint and the MCP
sidecar listed below, unchanged in intent.

- [ ] **6.1** Dev-only control endpoint over the runtime stimulus seam, gated by
  `Development` + `CLIMBER_DEV_CONTROL=1`, loopback + a per-process token.
- [ ] **6.2** MCP sidecar (`scripts/ui/`) exposing `emit_question`, `answer`, `read_transcript`,
  `screenshot`; `dotnet watch` keeps the app up.
- [ ] **6.3** Optional activity feed so a run's tests/edits appear in the app live.

## Lane 7 — serve producer (`[free]`, one `[live]` verification turn)

**Why.** Today the run is `opencode run --format json` and the question channel is a *second*
`opencode serve` child. Forms are process-local to the server that created them, so a form the run
raises never reaches a channel attached to a different server: live questions are observably
cancelled instead of answered. The producer and the channel must be the **same child**.

**Contract facts** (from the server's own `openapi.json`, v2.3.0, captured free at
`%TEMP%\climb-ui\serve-contract\openapi.json`):

- `POST /api/session` accepts a caller-supplied `id` (`^ses`); `GET /api/session/{sessionID}`
  returns `{data: Session.Info}` or 404. The app mints `ses_` + GUID, so: GET first, POST `{id}`
  only if absent. Create returns 200 (no 409 documented), so a duplicate `id` is unproven — the
  GET-first order makes it unreachable in practice.
- `POST /api/session/{sessionID}/prompt` takes `{text, resume?}`; the summary is explicit —
  "Durably admit one session input and schedule agent-loop execution **unless `resume` is false**",
  so omitting `resume` starts the turn. The one live turn only confirms the *sequence*, not this.
- `POST /api/session/{sessionID}/interrupt[?resume=true|false]` → `{interrupted: boolean}`;
  `false` is the documented idle no-op.
- Terminal: `session.execution.succeeded | failed | interrupted` (`interrupted.reason` is
  `user|shutdown|superseded|inactivity`), with `session.idle` as the quieter marker.
  `session.execution.started` brackets the turn.
- Text and reasoning: `started{ordinal}` → `delta{ordinal,delta}` → `ended{ordinal,text}` where
  `ended.text` is complete. **Emit one CLI-shaped line per block at `ended`**, not one per delta;
  the same rule for reasoning.
- Tools: `input.started{id,name}` (the tool name lives here) → `input.delta`/`input.ended` →
  `called{id,input,executed}` → `progress{id,metadata}` → `success{id,content[],executed}` /
  `failed{id,error{type,message,status},content[],executed}`. The CLI `tool_use` line carries
  `tool` and `state.status`/`input`/`output`, so the mapper must remember `name` from
  `input.started` and pair it with the outcome by `id`.
- Steps: `step.started{sessionID,assistantMessageID,agent,model,started}` and
  `step.ended{finish,cost,tokens{input,output,reasoning,cache{read,write}}}` — `RunMetrics.Add`
  already reads the CLI `step_finish` shape, so the mapping must keep those field names.
- Envelope on every frame: `{id, created, type, durable?, location?, data}`; SSE `event:` is empty
  and the JSON lives in `data:`.

**Open shape question for the user** — where the seam goes:

- **(A) `ServeRunRunner : IRunProcessRunner`** — a second implementation of the seam that already
  exists (`RunAsync(request, onLine, ct) → exit code`). `RunService` creates the child when the
  flag says `serve`, hands it to the runner *and* to the channel, and otherwise does not change.
  Smallest diff, existing runner tests keep meaning what they meant, and the whole migration stays
  one revert. Cost: the child is created outside the runner, so `RunService` owns the lifetime.
- **(B) `IRunProducer` with `CliRunProducer` + `ServeRunProducer`** — an explicit producer seam.
  Cleaner to read and lets the serve path grow options the CLI has no equivalent for (steer,
  interrupt reason). Cost: a new abstraction, a wider `RunService` diff, and the runner tests need
  a parallel set.

Recommendation: **(A)** for this lane — it is the reversible step the spec asked for; (B) can be
taken later if serve becomes the default producer and grows CLI-less features.

- [x] **7.1** `ServeRunRunner` (or `ServeRunProducer`): create the session if absent, POST the
  prompt, subscribe `GET /api/event`, map the allowlisted families above to the exact CLI line
  shapes `LogFormatter` parses, and return when `session.execution.*` lands for this session.
  Ignore every event whose `sessionID` is not ours, and every family not mapped (`permission.*`,
  `pty.*`, `shell.*`, `form.*` — the channel owns forms).
  *Tests:* synthesized payloads in a new `ClimberWeb.Tests/Fixtures/serve-events.jsonl`; one test
  per mapped line, asserting the emitted JSON parses through `LogFormatter.Parse` to the same
  `LogEntry` the CLI fixture produces for that step; a foreign `sessionID` emits nothing; an
  unknown family emits nothing.
- [x] **7.2** One child, two users. `QuestionChannel.StartAsync` gains an overload that takes an
  existing `ServeChild` and does not dispose it (`ownsChild: false`); `RunService` creates the
  child once in `serve` mode and disposes it in the run's `finally`, after the channel.
  *Tests:* the channel with a borrowed child never disposes it; the run's `finally` disposes
  exactly once; `off` mode still takes the old path (no child at all).
- [x] **7.3** Stop = interrupt, then close. `Stop()` cancels the token; the serve path POSTs
  `/interrupt`, waits briefly for `session.execution.interrupted`, then closes the child's stdin
  and disposes. A child that does not answer the interrupt is killed by `ServeChild.DisposeAsync`
  (already the behaviour).
  *Tests:* interrupt is POSTed once on cancel; a child that never emits `interrupted` still
  terminates within the grace period; exit status maps to `Stopped`, not `Failed`.
- [x] **7.4** Failure lines, mirroring the spec's table: child did not start → the existing
  `[questions unavailable: …]` line and the run continues on the CLI path; session create 4xx →
  `[serve: session could not be created (status)]`; prompt 4xx → `[serve: prompt refused (status)]`;
  a stream that ends before a terminal event → `[serve: the run's server closed the stream]` and
  exit code 1 so the run reports failed with the normal fallback offer.
  *Tests:* each status maps to its line and to `Failed`/`Stopped` as specified; no line carries the
  password or the loopback URL.
- [x] **7.5** Flag. `Climber:Questions=serve` now selects *both* the child and the producer;
  `off` keeps `opencode run --format json` byte-for-byte. Default stays `off`.
  *Tests:* `off` builds the CLI request and never a child; `serve` builds no CLI request.
- [x] **7.6** One **live** verification turn, only with approval: one cheap prompt on a scratch
  folder with the flag on, capturing the real event order (execution.started → step → text →
  execution.succeeded) and the CLI-shaped lines the mapper emits, plus one run where a form is
  raised and answered through the channel **on the same child**. Recorded in `verification.md`.
- [x] **7.7** Fold in the four non-blocking nits from the live-loop gate while `drive.mjs` is open:
  `text` waits out loading copy, `status` with no state file names a next action, `shot --help`
  prints help, and the dead SIGINT/SIGTERM block after the keep-alive promise is removed.
- [ ] **7.8** Gate the lane at **8** once 7.1–7.5 are green and 7.6 (or an explicit deferral) is
  recorded.

## Floor (every step)

```powershell
dotnet build HillClimberUI.slnx
dotnet test --nologo
# seeded host, another shell:
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:CLIMBER_SEED='<fixture|1>'
dotnet run --project ClimberWeb            # http://localhost:5179, PIN 1234
cd scripts/ui; node theme-nav.mjs; node security.mjs; node providers.mjs
```

`polish.mjs` is supporting evidence only. `surfaces.mjs` is **not run** without approval. Report
anything not run as "not run".

## Checklist

- [x] 0.1 spec + plan
- [x] 2.1–2.6 seam, supervisor, client, channel, wiring, flag
- [x] floor: build + `dotnet test` (700/700)
- [x] 3.1–3.3 row, contract block, `Home.razor` hook
- [x] 3.5.1–3.5.2, 3.5.4 committed replay seed + probes (3.5.3 → lane 6)
- [x] floor: build + `dotnet test` + theme-nav + security + providers + polish (100, 0/560)
- [x] hillclimb gate: **8/10** on pending + answered + unsupported at 900/375, both themes
  (7 → 8; record in `evidence.md`)
- [x] 4.1–4.3 persistence; gate **8/10** on the restored states (7 → 8; `evidence.md`)
- [x] 5.1–5.2 committed seed + evidence note
- [ ] 6.x future live harness (not this pass)