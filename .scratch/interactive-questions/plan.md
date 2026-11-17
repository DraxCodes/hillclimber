# Thread plan — interactive model questions

**Status:** lanes 0, 2, 3, 3.5 and 5 done; the row scored **8/10** and shipped. Lane 4 (persistence)
is next. **Date:** 2026-11-17. **Spec:** [spec.md](spec.md). **Handoff:** [handoff.md](handoff.md).

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

- [ ] **4.1** `StoredQuestion`/`StoredOption` + `StoredEntry.Question` + `From`/`ToEntry`.
  *Tests:* round-trip through `JsonSessionStore`; a legacy file still loads; no
  `password`/`url` key in the JSON.
- [ ] **4.2** Restore rules: pending/`Stale` → `no answer sent`, closed; answered closed.
  *Tests:* `SessionCoordinatorTests` — pending restores one closed non-interactive row;
  pending+answered restores one `answered:` row.
- [ ] **4.3** README row for the persistence shape.

## Lane 5 — evidence

- [x] **5.1** Commit the display seed as `ClimberWeb.Tests/Fixtures/questions.jsonl` (a fixture, not
  an `opencode run` recording), replayed by the existing `CLIMBER_SEED` host. No new `questions.mjs`
  was added: the probes stayed throwaway under `%TEMP%`, and `PlaybackFixtureTests` pins the states.
- [x] **5.2** Read-through and both scorecards recorded in
  `.scratch/interactive-questions/evidence.md`; the gatekeeper scored them 7 → 8.

## Lane 6 — future: live app harness (not built now)

- [ ] **6.1** Dev-only control endpoint over the runtime stimulus seam, gated by
  `Development` + `CLIMBER_DEV_CONTROL=1`, loopback + a per-process token.
- [ ] **6.2** MCP sidecar (`scripts/ui/`) exposing `emit_question`, `answer`, `read_transcript`,
  `screenshot`; `dotnet watch` keeps the app up.
- [ ] **6.3** Optional activity feed so a run's tests/edits appear in the app live.

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
- [ ] 4.1–4.3 persistence
- [ ] 5.1–5.2 committed seed + evidence note
- [ ] 6.x future live harness (not this pass)