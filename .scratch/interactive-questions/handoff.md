# Handoff — interactive model questions

**Branch:** `feat/interactive-questions` (12 commits). `master` is back at `23be0ba`
(`= origin/master`) and carries none of this. Nothing is pushed.

**Where it stands:** the transport seam, the row, its evidence, and persistence are
built, gated and committed. The live row scored **8/10 — Clean & Clear** (7 → 8); the
restored states scored **8/10** on their own pass (7 → 8). Lane 6 (live-app harness)
stays deferred.

## Commits on the branch

| Commit | What |
| --- | --- |
| `2a64d92` | spec, plan, council and verification docs |
| `2e447eb` | question contract, formatter and grouper (`QuestionContract.cs`) |
| `acc1c2e` | `OpenCodeServe.cs` — per-run `opencode serve --stdio` child |
| `9543c5e` | `OpenCodeFormClient.cs` — form REST + SSE, envelope unwrap, outcome table |
| `6dccc82` | `QuestionChannel.cs` — allowlist, reconcile, backoff, submit/cancel/check |
| `49d72c8` | `RunService` wiring + three `IRunService` members |
| `c2c0378` | `QuestionTransport.cs` + `Climber:Questions` (`off`/`serve`, default off) |
| `1c61854` | the row: `AiQuestion`, `QuestionOptionRow`, `QuestionView`, `Home` hook, fixture, Phase 2i |
| `95ee8bc` | gate result, evidence map, plan/spec amendments |
| `ecadb55` | handoff |
| `6a3f736` | persistence: `StoredQuestion`/`StoredOption`, restore rules, tests |
| `8ca44a8` | README row for the persisted shape |
| `7245fd9` | restored rows drop the live `asks:` marker (gate fix) |

## What is deliberately NOT built

- **Producer migration.** `Climber:Questions` stays `serve`-gated and off by default; runs still use
  `opencode run --format json`. Flipping every run onto the serve producer is a separate reversible
  change once the mapping is proved.
- **`IQuestionStimulus`** (runtime arrival + fake channel). Deferred to lane 6; the committed
  `CLIMBER_SEED` fixture is the evidence instead.
- **The live-app harness** (lane 6).
- **A general permission UI.** `permission.*` events are dropped by the channel allowlist before
  they become state, and no copy contains `permission`, `allow` or `deny`.

## Lane 4 — done (persistence)

- `StoredEntry` carries an optional `StoredQuestion` (form id, status, title, field key,
  kind, options, chosen value, notice, kind label). Only display data persists: no URL,
  password, header, raw frame or run session id. `From`/`ToEntry` round-trip it, and a
  legacy file with no `Question` still loads.
- `QuestionState.Restored` + `QuestionRestore.Apply`: a stored `Pending` or `Submitting`
  restores as `Stale` (`no answer sent`); every restored row is inert. `QuestionView`
  drops the actions and the live `asks:` marker; the answered row stays folded closed.
- One row per form id comes from `QuestionLines.Collapse` in `LogGrouper.Group`, so a
  stored pending + answered pair shows one `answered:` row.
- Tests beside `SessionStoreTests`, `SessionCoordinatorTests` and `QuestionViewTests`;
  README Sessions bullet records the shape.

## Next: lane 6 — live-app harness

Not built, and not needed for lane 4. See `plan.md` lane 6: a dev-only control endpoint
(Development + `CLIMBER_DEV_CONTROL=1`, loopback + per-process token), an MCP sidecar in
`scripts/ui/`, and optional live activity. `surfaces.mjs` still needs approval before it
runs (paid).

## Environment

```powershell
# floor
dotnet build ClimberWeb
dotnet test --nologo -v minimal        # 712 tests, ~1 s

# seeded host (Development only)
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:CLIMBER_SEED='questions.jsonl'
$env:ASPNETCORE_URLS='http://localhost:5179'; dotnet run --project ClimberWeb --no-launch-profile
# http://localhost:5179, PIN 1234

# restore host: no CLIMBER_SEED (playback uses MemorySessionStore), temp session folder
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:Climber__SessionsPath="$env:TEMP\climb-ui-lane4-sessions"
$env:ASPNETCORE_URLS='http://localhost:5179'; dotnet run --project ClimberWeb --no-launch-profile

# free harnesses (server must be up)
cd scripts/ui; node theme-nav.mjs; node security.mjs; node providers.mjs; node polish.mjs
# surfaces.mjs drives real opencode runs: ask before running it
```

Traps that cost time last session:

- **`wwwroot/tw.css` is a committed build artifact.** Any new Tailwind utility silently does
  nothing until you run `npm run css` in `ClimberWeb/`. A whole "designed" row rendered unstyled
  because of this.
- **Stop `ClimberWeb` before `dotnet build`/`dotnet test`**, or MSB3027/MSB3021 fails on the
  locked `bin\Debug\net11.0\ClimberWeb.exe`. `dotnet test --no-build` is the workaround while the
  host runs.
- **`CLIMBER_SEED` takes a filename or an absolute path, no extension appended**: `questions`
  fails, `questions.jsonl` works. Fixtures live in `ClimberWeb.Tests/Fixtures/` and are copied to
  the test output.
- Playwright is only in `scripts/ui/node_modules`; throwaway probes import
  `file:///C:/Users/joelp/code/HillClimberUI/scripts/ui/node_modules/playwright/index.mjs`.
- When appending a JSONL fixture in PowerShell, do not use `Add-Content` on a file without a
  trailing newline (it glues two objects onto one line and the line is then dropped), and write
  UTF-8 **without BOM** (`Set-Content -Encoding utf8` in PS 5.1 adds one).
- **A seeded host can never show a restored session.** `CLIMBER_SEED` registers
  `MemorySessionStore` (Program.cs), so `StoredEntries()` is empty. Verify restore on a
  second Development host without the seed, with `Climber__SessionsPath` pointing at a
  throwaway folder holding a hand-written session file. The file is UTF-8, PascalCase
  (`Summary`, `Entries`, `Question`), enums as strings, and the entry's `Status` is
  `StepStatus` while `Question.Status` is the question's own status.
- **A screenshot's CSS viewport is whatever the page last had.** Set `page.setViewportSize`
  before `page.screenshot`; at 2x device scale the PNG must be exactly twice the intended
  width. A 1280 image labelled 900 is a gate failure even when the state looks right.

## Design facts learned the hard way (do not rediscover)

- **Blazor renders a parent before its children's `OnAfterRenderAsync`.** A component that needs a
  child's `ElementReference` cannot use it in its own `firstRender` pass; keep a `bool` and focus on
  the first pass that has the reference (the parent re-renders because the child's `EventCallback`
  fires `StateHasChanged` on it).
- **`@ref` lambdas do not compile in this project** (CS0201, element or component). A child reports
  its `ElementReference` through an `EventCallback`.
- **Preflight is off** (`docs/ai-elements.md`), so Tailwind's bare `border` resolves to
  `currentColor` — a near-black box. Always name `border-border` on this layer.
- **`--color-accent` equals `--surface-2`, the transcript pane's own fill**, so `hover:bg-accent`
  is invisible in the feed. Use `bg-card` (`--surface`) for a hover that steps off the pane.
- **Feed rhythm is asserted by `polish.mjs`**: rows 8px apart, a 24px group step for anything with
  `data-kind` in `.log-item[data-kind=…]` (`tw.input.css`). A new row kind must be added to that
  rule or the harness fails.
- **`polish.mjs` cannot see `oklab()` colours**, which is what `text-muted-foreground/70` computes
  to; it skips them, so a 100/560 score says nothing about the alpha tier. Measure contrast with a
  compositing helper instead.
- The row's interaction: options are `role="radio"` buttons with a roving tabindex, Enter/Space
  arrive as the button's own click (one commit path for pointer and keyboard), and the `›` gutter
  is the cursor — suppressed while the row is busy, because nothing is actionable mid-submit.

## Recorded conditions (not flaws, already accepted)

- **The replay host answers nothing.** `PlaybackRunService.AnswerQuestionAsync` returns
  `Disconnected` and no channel publishes `Submitting`, so a seeded click is inert. A *live*
  `Disconnected` folds the row to `questions unavailable` (`QuestionChannel.SubmitAsync`, default arm).
- **Not captured visually as *live* rows:** `refused`, `unconfirmed`, `unavailable`, and live
  boolean options. Copy is pinned by `QuestionViewTests`; the boolean busy row was seen only in a
  throwaway seed. Restored `refused`/`unconfirmed` rows *were* captured in lane 4.
- **EXCEPTION 1** in `docs/ui-contract.md` covers the reverse feed: tab order runs newest → oldest,
  and the top of the 900 shot is blank.

## Evidence to reuse

- `.scratch/interactive-questions/evidence.md` — scorecards for the live row and lane 4, seed/shot
  maps, read-through, floor.
- `ClimberWeb.Tests/Fixtures/questions.jsonl` — five live row states, replayed by `CLIMBER_SEED`,
  pinned by `PlaybackFixtureTests.TheQuestionsFixtureReachesEveryRowState`.
- Throwaway shots and probes: `%TEMP%\climb-ui-shots\questions\` (live row) and
  `%TEMP%\climb-ui-shots\questions-restored\` (lane 4); `%TEMP%\climb-ui\lane4-restored.mjs`
  (shots + counts) and `lane4-readthrough.mjs` (tab walk, fold, contrast).
- Throwaway stored session: `%TEMP%\climb-ui-lane4-sessions\ses_restoredquestions.json` (seven
  question lines over six form ids). None of these are committed; they will not survive a machine
  move, so regenerate from `evidence.md` if needed.