# Spec: Output panel log formatting (Chain of Thought + Terminal)

## Context

The [03] output panel renders a run's live opencode transcript. The previous slice
(`.scratch/ai-elements-output/spec.md`) introduced `LogFormatter`, `LogGrouper`,
`ToolPresentation` and the `Ai/` components: prose is a message, contiguous tool calls are
one trace group, failures are red and pre-opened, unknown events render verbatim.

That slice left three agent-log problems unsolved, and this one attacks them with the
hillclimb loop in `docs/agents/hillclimb-rubric.md`:

- `reasoning` events are not parsed, so a thinking model's transcript shows raw JSON lines
  on the normal path.
- A shell call is indistinguishable from any other tool: the title truncates the command
  at 80 characters, the body is output-only, no exit status is shown, and nothing can be
  copied.
- There is no turn structure. `step_start` is invisible, `step_finish` is a verbose marker,
  and activity rows pile up with no summary; after scrolling up there is no way back to
  the live edge.

shadcn [Chain of Thought](https://ui.shadcn.com/docs/ai-elements/base/chain-of-thought)
and [Terminal](https://ui.shadcn.com/docs/ai-elements/base/terminal) are the reference
compositions, built on the [Trace](https://ui.shadcn.com/docs/components/base/trace)
primitive: a summary row that expands into steps, and a command row with status and output.

## Decisions (locked with the user)

1. **Scope: formatting + parser.** Parse `reasoning` events and opencode's human
   `part.state.title` / exit metadata. Keep the `Plain` fallback verbatim.
2. **A step is one turn.** A `step_start` → `step_finish` turn's activity (reasoning,
   tool and subagent rows) collapses into one summary; prose messages stay visible outside
   it; `step_finish` is the turn's cost/token footer.
3. **Terminal:** command card with a human title, a full `$ command` line, status/exit and
   a copy affordance. Output is not capped at 12 lines: the card has a sensible
   `max-height` and scrolls.
4. **History traversal:** jump-to-latest only, shown when scrolled away from the live edge.
   No filter chips, no session sidebar.
5. **Target score: 8.** Not 10.

## Target score

8 (rubric default; the task did not ask for 10).

## Goal

The panel reads as a modern agent transcript: every turn is one Chain-of-Thought row that
expands into the work it did, reasoning is readable text rather than JSON, shell steps read
like terminal cards that can be copied, and a person who scrolls back can return to live
output in one click. Unknown events still render verbatim.

## Approach

### 1. Parsing

`LogKind` gains `Reasoning` and `Unrecognized`. `LogFormatter` reads `type: "reasoning"`
and takes `part.text`; an empty thought is still classified, but the grouper skips it so
nothing renders blank. Model-written text (reasoning, error messages, event names) is
stripped of bidi and non-whitespace control characters and bounded before display.

A JSON event with a string `type` but no `part` is an error when the type is `error` (the
real opencode shape carries `error.data.message` at the root); otherwise it stays `Plain`.
An event with a `part` whose type this renderer does not know becomes `Unrecognized`: one
quiet row whose raw payload is available behind a disclosure, so the data is still there
without filling the feed with debug JSON. The row is titled by its own event type as
lowercase prose (`session.updated` reads `session updated`); it never says "unrecognised
event", which describes the renderer's gap rather than the run.

`LogEntry` carries opencode's human tool title (`part.state.title`) and an optional exit
code from `part.state.metadata.exit` (numeric). Both are optional and degrade to today's
behaviour.

`SummarizeFinish` loses the internal event name and cache counters: the footer reads
`stop · $0.0002 · 221 in / 116 out`, with a short cache note only when non-zero.

### 2. Grouping

`LogGrouper` folds a turn:

- `StepStart` opens a turn (not rendered).
- `Reasoning`, `Tool` and `Subagent` entries join the open turn.
- `Text` stays outside the turn as a `Message`, in chronological position.
- `StepFinish` closes the turn and becomes its footer; an unterminated turn (a run still
  streaming) is emitted at the end.
- `Error` and `Plain` break the turn and render on their own.

`OutputItemKind.Turn` carries the turn's entries. The summary counts tool/subagent rows
(`Worked in 3 steps`), appends `· 1 failed` when any row failed, and opens automatically
while the turn is running or when a row failed.

### 3. Presentation

`ToolPresentation` gains a terminal view for `shell`/`bash`: human title from
`state.title` when it is not just the tool name, otherwise a derived verb-plus-subject;
the full command; status/exit; and the output. Non-shell tools keep the trace-row shape.

### 4. Components

- `AiTurn` — one `<details>`: summary line, body of activity rows, optional footer.
- `AiReasoning` — collapsed `<details>`, dim prose body.
- `AiTerminalCard` — title, `$ command`, status/exit, copy button in the card header,
  scrollable output body. The header is a `<summary>`, so the copy button sits inside an
  interactive element; an activation on it does not toggle the card (verified in the
  keyboard probe), which keeps the button before the disclosure in tab order. Moving it
  outside the summary would either put it after the output body or need a non-disclosure
  header layout, so the deviation is kept and recorded here.
- Existing `AiTraceLine`, `AiMessage`, `AiMarker`, `AiCodeBlock` are reused; `AiTraceGroup`
  retired once the turn replaced it.

All components take primitives, never `LogEntry`.

### 5. Copy and jump-to-latest

One small inline script in `App.razor` (event-delegated, like the theme script) provides
`climberCopy(text)` and listens for scroll (capture phase) on `#log-feed`. The feed is
`column-reverse`, so the live edge is `scrollTop === 0`; the header's jump-to-latest button
uses `Math.abs(scrollTop) > 32`, is hidden via the `hidden` attribute when at the edge (so it
leaves the tab order), and returns with `scrollTop = 0`. It sits in the output panel header
rather than floating over the feed, so it never covers the row it points at.

### 6. Styling

`ai.css` only, using the tokens in `app.css` (`--surface`, `--surface-2`, `--ink`,
`--ink-2`, `--ink-3`, `--line`, `--line-2`, `--accent`, `--accent-text`, `--green`,
`--orange`, `--red`, `--focus`, radii, fonts). No new palette, no Tailwind, no icon
package. Card height is chosen against the seeded 200-line output at 900 and 375.

## What it should do

1. A `reasoning` event renders as a collapsed, readable thinking block, never raw JSON.
2. A turn renders as one summary row that expands to its reasoning and steps; prose stays
   visible; a running turn is open; a failed row marks the summary.
3. A shell step renders as a terminal card with a human title, the full command, status
   and exit, a scrolling body, and copy feedback.
4. Copy works with the keyboard and announces success or failure in text.
5. A run with no `step_finish` still emits its last turn.
6. A reasoning event with no text renders nothing rather than a blank row; a non-JSON
   line still renders verbatim; an unknown opencode event keeps its payload behind a
   quiet disclosure.
7. Scrolling away from the live edge reveals jump-to-latest; activating it returns and
   hides the control.
8. Everything fits at 375 and 900 in both themes; focus rings match the existing controls.

## Out of scope

- Filter chips, a session sidebar, cross-run history, virtualization, search.
- Markdown rendering, diffs, test results, file tree, plan/queue events.
- The composer, provider bar, Activity page.
- Changing `ClimbState`, the run loop or the log feed.
- The paid `surfaces.mjs` run.

## How we know it is done

- `dotnet test` green with new formatter, grouper and presentation cases.
- Free harnesses pass: `theme-nav`, `security`, `providers`; `polish.mjs` UPS recorded as
  supporting evidence.
- Seeded screenshots at 900 and 375, light and dark, plus open/copied/jump states, and a
  first-run + keyboard read-through.
- `quality-gatekeeper` scores 8 or above from that evidence.
- Seed removed, `git status` clean of it, app stopped.

## Risks

- `column-reverse` scroll sign differs across engines; use absolute distance.
- Native `<details>` state across Blazor re-renders; keep `@key` and omit `open` except for
  running/failed.
- Clipboard API may be unavailable on `http://localhost`; fall back to a visible failure.
- The real exit-code key may differ; confirm against a captured event and degrade to no
  exit rather than guessing.
- Turn collapse could hide the answer; prose must stay outside the turn.

## Evidence (to fill per iteration)

Seeded captures live in `%TEMP%\climb-ui-shots` and are never committed. The seed is
`ClimberWeb/SeedSampleLogs.cs` plus one `ActiveLogs` hook in `Home.razor`; both are removed
before the final report.

| Iteration | Change | Score | Verdict |
| --- | --- | --- | --- |
| baseline (`aa3cff0`) | raw JSON on the normal path, no turn structure | 4 | — |
| 1 | reasoning and unknown events rendered, no raw JSON | 6 | KEEP |
| 2 | terminal card, human titles, exit code, copy, Escape | 6 | KEEP (no delta) |
| 3 | one turn per step boundary, footer, quiet unknown row | 7 | KEEP |
| 4 | per-row status colour, 24px copy target, jump-to-latest, quiet fallback row | 7 | KEEP (no delta) |
| 5 | stopped turns settle, subagent tab reads top-down, closed disclosures unlaid | 7 | STOP (two flat iterations) |
| 6 | tab switch keeps open rows and scroll, solo feed sizes to rows, title wraps, head count dropped | 8 | SHIP |

Read-through and probe results for iteration 4 (`i4c-*` shots):

- 0 console errors; 0 raw-JSON plain lines; 2 turns (`Working in 2 steps`,
  `Worked in 5 steps · 2 failed`); footer
  `tool calls · $0.000208 · 221 in / 116 out · cache 7296 read · 17 reasoning`.
- Copy: 32×24 px, label `copied`, clipboard exact (`dotnet watch test --project …`), the
  card does not toggle; Escape closes an open disclosure and returns focus to its summary.
- Jump to latest: hidden at the live edge, shown when scrolled back (24 px tall in the
  panel header, so it never covers a row), returns `scrollTop` to 0 and hides itself.
- Status colour: a successful `read` and a successful subagent row inside the failed turn
  compute to the normal ink; only the failed rows and the failed summary are red.
- Subagent tab: one `plan-architect subagent` row with its prompt, no turn chrome and no
  false "Working" state.
- Floor: `dotnet test` 341 passed, 0 failed; `theme-nav`, `security` and `providers` pass;
  `polish.mjs` 560 checks, 0 defects, UI polish score 100. `surfaces.mjs` (paid) not run.

Known and deferred: disclosure state (turn, reasoning, terminal) resets when the reader
switches Output tabs, because the tab switch re-renders the feed. (Resolved in iteration 6.)

Iteration 5 (`i6-*` shots) answers the two flaws iteration 4 left:

- A turn with no `step_finish` reads as history once its run stops: the seeded interrupted
  turn reads `Stopped after 2 steps` (collapsed) instead of a shimmering `Working in 2
  steps` beside an `idle` status card, and a row left with status `running` renders as
  settled text. The check is a statement about the run, not about the event stream.
- A subagent tab reads top-down with its prompt at full width (`plan-architect subagent`
  plus the whole prompt) instead of one row pinned under a blank panel.
- Closed disclosures are no longer laid out (`display: none` on the closed body), so folded
  work costs no layout and the polish harness stops measuring rows a reader cannot reach:
  `polish.mjs` back to 560 checks, 0 defects, UI polish score 100.
- 344 tests pass, 0 failed. `theme-nav`, `security` and `providers` pass. `surfaces.mjs`
  (paid) not run.

Iteration 6 (`i7-*` shots) closes the two defects the gatekeeper named at STOP, rather
than polishing:

- Switching Output tabs keeps the reader's place. The open disclosures and scroll offset
  are saved per tab from a capture-phase click listener (`data-outtab` marks the tab
  buttons, `data-active-tab` the feed) and restored by `climberLog.restoreTab` from
  `OnAfterRenderAsync` after Blazor patches the new rows. Probe: opening a turn and a
  command card, scrolling to `scrollTop -180`, switching to `Plan Architect` and back
  gives `sameScroll: true`, `sameOpen: true`, `sameDetails: true` (10 disclosures, 4 open
  on both sides). This also makes the delivered jump-to-latest survive a tab round trip.
- `.log.solo` drops the `46dvh` height and sizes to its rows: the one-row subagent tab is
  74 px of feed holding 74 px of rows (0 % blank, was ~86 %), 164 px at 375.
- The two nits: a trace title wraps instead of ellipsizing on a panel narrower than
  560 px (probe reports 0 clipped titles at 375; a solo prompt takes its own full-width
  line), and the header's raw `18 events` count is gone — the head is `[03] output` with
  the `latest` pill right-aligned.
- 344 tests pass, 0 failed. `theme-nav` 0 failures, `security` 0 failures, `providers`
  15 checks 0 failures, `polish.mjs` 560 checks 0 defects / UPS 100. `surfaces.mjs`
  (paid) not run. 0 console errors, 0 horizontal overflow in the feed at 375 and 900.

## Review follow-ups (`40bfae3...HEAD`)

The two-axis review found three defects worth fixing before shipping; the rest (the
`ai-status-*` switch repeated across three components, the `TurnPresentation` verb
ternary, the `pending`/`message` buffer names, `TraceView`'s nullable pair, the
`ReadCommand`/`ReadExitCode` key loops, the shared signaling-only glyph, and the `.solo`
rule reaching from `ai.css` into a page class) are recorded as smells for a later pass.

1. `Settle` moved out of `Home.razor` into `TurnPresentation.Settled(entries, live)` /
   `Settle(entry)` with tests, per `docs/ai-elements.md` (pure transforms live in a tested
   service, not the page).
2. Prose arriving during a turn now closes that turn and renders in place, then the work
   after it opens the next turn — chronological, and still never folded. A `step_finish`
   that lands after prose attaches to the turn it closed; only a step that produced nothing
   but prose leaves a quiet cost marker.
3. An unknown event inside a turn folds in as its quiet row instead of splitting the turn;
   between turns it still stands alone. The turn body routes it through
   `ToolPresentation.ForEvent`, so it never reads "Ran session.updated".

Floor after the follow-ups: 349 tests pass, 0 failed.

## Live demo (unseeded, two real Surplus runs)

A "run" against this repo ("read GLOSSARY.md, then README.md") exercised the shipped path end
to end, 35–36 s per run:

- Two turns, `Worked in 1 step` and `Worked in 2 steps · 1 failed`; the missing
  `GLOSSARY.md` reads `Failed to read …GLOSSARY.md` in red and opens its turn, and the
  README read beside it keeps the ordinary ink.
- Narration lands where it happened: `I'll read both files.` sits above the two reads, and
  the final answer is its own message after the last turn.
- No raw JSON at the live edge; the only `Plain` lines are the runner's own
  (`$ opencode run …`, `===== Completed =====`, `Spend this run: est. $…`), which is the
  fallback working as designed.
- A prose-only step's cost line reads exactly like a turn footer
  (`stop · $0 · 168 in / 307 out · cache 11776 read · 17 reasoning`), with no event
  timestamp.
- Keyboard: Enter opens a disclosure, Escape closes it and returns focus to its summary.
  0 console errors; feed at the live edge with jump-to-latest hidden.

Follow-up found by the demo: `AiMessage` renders markdown as literal text, so a real answer
shows `**bold**` and backticks verbatim. Seeded prose never contained markdown, so the
hillclimb never surfaced it; it needs its own spec (which markdown subset, and whether inline
only or block too).

## Fixed point for review

Session start HEAD is `40bfae3`. Review with `/code-review 40bfae3`.