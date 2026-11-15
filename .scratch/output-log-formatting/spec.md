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
without filling the feed with debug JSON.

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
- `AiTerminalCard` — title, `$ command`, status/exit, copy button outside the summary,
  scrollable output body.
- Existing `AiTraceLine`, `AiTraceGroup`, `AiMessage`, `AiMarker`, `AiCodeBlock` are
  reused; `AiTraceGroup` retires if the turn fully replaces it.

All components take primitives, never `LogEntry`.

### 5. Copy and jump-to-latest

One small inline script in `App.razor` (event-delegated, like the theme script) provides
`climberCopy(text)` and listens for scroll on `#log-feed`. The feed is `column-reverse`, so
the live edge is `scrollTop === 0`; the button uses `Math.abs(scrollTop) > 32`, is hidden
via the `hidden` attribute when at the edge (so it leaves the tab order), and scrolls back
with `scrollTop = 0`.

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

Baseline and per-iteration scores, shots, read-through and floor output go here.

## Fixed point for review

Session start HEAD is `40bfae3`. Review with `/code-review 40bfae3`.