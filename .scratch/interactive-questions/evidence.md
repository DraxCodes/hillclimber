# Evidence — the question row

2026-11-17. Surface: `ClimberWeb/Components/Ui/Ai/AiQuestion.razor` with
`QuestionOptionRow.razor` and the pure `QuestionView.cs`, rendered by
`Home.razor` for `OutputItemKind.Question`. Target: **8** (rubric default; the
spec does not demand a 10).

## Seed

- **Committed (product code):** `ClimberWeb.Tests/Fixtures/questions.jsonl`,
  replayed by the existing `CLIMBER_SEED` host (`CLIMBER_SEED=questions.jsonl`).
  One transcript reaches `unsupported`, `cancelled`, `stale`, `answered`,
  `pending`, and it is pinned by
  `PlaybackFixtureTests.TheQuestionsFixtureReachesEveryRowState`, so a formatter
  or grouper change cannot rot the evidence silently.
- **Throwaway (uncommitted, one file outside the repo):**
  `%TEMP%\climb-ui\questions-busy.jsonl` is the committed fixture plus one
  boolean `Submitting` row, for the busy state the fixture cannot reach. Not
  committed, because a recording that stops mid-submit reads as a stuck row.

## Shots

`%TEMP%\climb-ui-shots\questions\`, 2x, 900x1200 and 375x812, light and dark:

| Files | State |
| --- | --- |
| `row-pending-*`, `question-*-light/dark.png` | pending, arrival focus on option 1 |
| `row-pending-hover-*` | pending with the pointer on option 2 (`bg-card` fill) |
| `row-submitting-*` | busy: disabled options, spinner, `sending…`, `aria-busy` |
| `row-unsupported-*` | `asks:` + notice + `dismiss` chip |
| `row-answered-*`, `row-answered-open-*` | folded / expanded answered row |
| `row-cancelled-*`, `row-stale-*` | settled rows with the question glyph |

## Read-through

Keyboard: the first option takes focus on arrival and the sr-only status line
says `question from the run · focus moved to Keep the live dot`; ArrowDown moves
the roving cursor and `tabindex` reads `0/-1/-1`; Enter and Space arrive as the
option button's own click, the same path as the pointer; Tab reaches the answered
fold's trigger with a visible ring and Enter/Space toggle `aria-expanded`; the
band reads `awaiting answer` while a question waits and no `aria-busy` shows when
idle. Pointer: hovering an option lifts the whole row to `--surface` over the
pane, cursor is a pointer, and the three options are one large target each.
Width: no overflow or clipping at 343 (375) or 288 (320) content width, both
themes.

## Scorecards

### Iteration 1 — 7/10, Nearly clean

Held at 7 by two flaws, both found by measuring rather than looking:

1. **Option hover was a no-op.** `hover:bg-accent` resolves to
   `--color-accent: var(--surface-2)`, the transcript pane's own fill: a pixel
   diff of one option at rest vs hovered changed 1.07% of pixels, all inside the
   glyphs, and the cursor stayed an arrow while the secondary fold trigger above
   showed `cursor: pointer`.
2. **The 11px meta tier was under the bar this repo already sets.** `none fit?
   answer via the composer.` measured 2.71:1 light / 3.04:1 dark, `↑↓ move ·
   enter commits` 3.21/3.55 — the only copy that teaches the composer fallback.
   `polish.mjs` cannot see it: it skips `oklab()` colours, so a 100/560 UPS said
   nothing about those lines.

### Iteration 2 — 8/10, Clean & Clear (+1)

- Hover became `hover:bg-card` (the `card` token, i.e. `--surface`) plus
  `cursor-pointer` on the options and the `dismiss` chip. Rest vs hovered now
  diffs 98.7–99.5% of the row's pixels at both widths and themes; no new colour
  enters the palette.
- The alpha on the meta tier was dropped: all four lines resolve to `--ink-3` at
  **4.67:1** light / **4.8:1** dark, above the 4.5:1 the harness asserts.
- The busy state gained visual evidence, and the `›` cursor is now suppressed
  while the row is busy (`Focused && !Disabled`): the marker means "Enter here",
  and nothing is actionable mid-submit. Focus stays on the disabled option with
  its ring, so the reader is not moved.
- Answered fold: expanded title `text-base`/500 to match the pending title, and
  the option list is a `role="list"` of `listitem`s with one `→ … chosen`.

No level-7 edge remains on the primary path: every state has its own
composition, every visible string clears the contrast bar, and the row's three
controls share one hover/cursor language.

## Floor

| Command | Result |
| --- | --- |
| `dotnet test --nologo -v minimal` | Passed 700, Failed 0, Skipped 0 |
| `dotnet build ClimberWeb` | 0 warnings, 0 errors |
| `node scripts/ui/theme-nav.mjs` | FAILURES: 0 |
| `node scripts/ui/security.mjs` | FAILURES: 0 |
| `node scripts/ui/providers.mjs` | 15 checks, 0 failures |
| `node scripts/ui/polish.mjs` | 100 (0 defects in 560 checks) |
| `node scripts/ui/surfaces.mjs` | **not run** (paid; no approval) |

## Conditions recorded, not flaws

- **The replay host answers nothing.** `PlaybackRunService.AnswerQuestionAsync`
  returns `Disconnected` and no channel publishes `Submitting`, so a click on a
  seeded row is inert. The live path is pinned by `QuestionChannelTests` and the
  round-trip in `verification.md`; a live `Disconnected` folds the row to
  `questions unavailable` (`QuestionChannel.SubmitAsync`, default arm).
- **Not captured visually:** `refused`, `unconfirmed`, `unavailable`, and the
  boolean options on a *live* row. Their copy is pinned by `QuestionViewTests`;
  the boolean busy row was seen only in the throwaway seed.

# Lane 4 — persistence (restored rows)

2026-11-17. Surface: `StoredQuestion`/`StoredOption` on `StoredEntry`, the
`QuestionRestore` read rule, and `QuestionView.For` rendering restored rows.
Target: **8** (rubric default).

## Seed

`CLIMBER_SEED=questions.jsonl` cannot reach restore: the playback host registers
`MemorySessionStore`, so a seeded run never calls `StoredEntries()`. Two
Development hosts were used instead:

- **Live regression:** the committed `CLIMBER_SEED=questions.jsonl` host, unchanged.
- **Restore (throwaway, outside the repo):**
  `%TEMP%\climb-ui-lane4-sessions\ses_restoredquestions.json` — a hand-written
  stored session with **seven question entries over six form ids**: restored
  `Pending`, `Stale`, `Unsupported` (`number answer required`), `Refused`,
  `Unconfirmed`, and a `Pending` + `Answered` pair for one form id, so restore
  and the one-row-per-form collapse are both exercised. Plus one prompt and one
  prose row. Never committed: it is page-local state the committed fixture
  cannot reach.

## Shots

`%TEMP%\climb-ui-shots\questions-restored\`, 2x:

| File | State |
| --- | --- |
| `seeded-live-900-light.png` | live pending row, edge/options/`asks:`/`awaiting answer`; unsupported still has `dismiss` |
| `restored-900-light.png`, `restored-375-light.png` | six restored rows, light, 900×1200 / 375×812 CSS |
| `restored-900-dark.png`, `restored-375-dark.png` | same, dark |
| `restored-900-dark-answered-open.png` | answered fold open: title, `→ Violet`, `Amber` |

All restored rows use the settled `message` glyph. Pending and stale read
`no answer sent`; unsupported/refused/unconfirmed keep their notice and no
action; answered folds closed to `answered: Violet` and opens on demand.

## Read-through

Tab: lock → answered fold → folder → composer; no restored row enters the tab
order (zero `role="radio"`, zero `dismiss`/`check` chips). Enter/Space toggles
the answered fold (`aria-expanded` false→true→false). DOM counts: two
`no answer sent`, zero `awaiting answer`, six rows from seven question lines,
one `answered: Violet`. Restored title and micro copy measure **4.67:1 light /
4.8:1 dark** (compositing helper; `polish.mjs` skips `oklab()`).

## Scorecards

### Iteration 1 — 7/10, Nearly clean

Held at 7 by two flaws:

1. **Restored terminal rows kept the live `asks:` marker.** Unsupported,
   refused and unconfirmed rows dropped their dead actions but still read as an
   open ask with no way to act; `asks:` is app copy defined as the live marker
   in Phase 2i. Three of six restored rows.
2. **`restored-900-light.png` was a 1280 CSS capture.** The probe took the
   light shot before resizing. The 900-light restored state was unseen, which
   alone invalidates a score at 8.

### Iteration 2 — 8/10, Clean & Clear (+1)

- `QuestionView.For` sets `ShowPrefix = false` when `state.Restored`; restored
  rows keep the settled glyph, their notice and no action. Pinned by
  `ARestoredTerminalRowKeepsItsCopyButDropsTheLiveMarkerAndActions`; live rows
  stay pinned to `asks:` by `AnUnsupportedShapeOffersOnlyDismissAndNamesWhatIsNeeded`.
  Recorded in `docs/ui-contract.md` Phase 2i and spec § Persistence.
- The probe now sets 900×1200 before the light capture; PNG headers confirm
  **1800×2400 px = 900×1200 CSS** for both 900 shots, 750×1624 for the 375 pair.

No level-7 edge remains: all six restored states share one composition at both
widths and themes, no restored row offers an action, the only control is the
answered fold with the same trigger language as the live fold, and every visible
string clears the contrast bar. The distance to 9 is art-direction flatness
(uniformly muted history) and the recorded reverse-feed exception, not a flaw
in this lane.

## Floor

| Command | Result |
| --- | --- |
| `dotnet build ClimberWeb` | 0 warnings, 0 errors |
| `dotnet test --nologo -v minimal` | Passed 712, Failed 0, Skipped 0 |
| `node scripts/ui/theme-nav.mjs` | FAILURES: 0 |
| `node scripts/ui/security.mjs` | FAILURES: 0 |
| `node scripts/ui/providers.mjs` | 15 checks, 0 failures |
| `node scripts/ui/polish.mjs` | 100 (0 defects in 560) — supporting |
| `node scripts/ui/surfaces.mjs` | **not run** (paid; no approval) |
| `npm run css` | **not run** — the change adds no Tailwind class |

## Conditions recorded, not flaws

- **The reverse feed's top clip at 375** is `EXCEPTION 1` in
  `docs/ui-contract.md`, pre-existing; the oldest restored row sits partly
  above the scrolled viewport.
- **The open answered fold is captured at 900 dark only.** The 375/light cell
  uses the same `TraceCollapsible` and option markup already exercised by the
  live-row evidence.
- **The stored question object is display-only.** `SessionStoreTests` prove a
  saved file contains no `password`, `authorization`, `http` or `SessionId`
  key, and a legacy file without `Question` still loads.