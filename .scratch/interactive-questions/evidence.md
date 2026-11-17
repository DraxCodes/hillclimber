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