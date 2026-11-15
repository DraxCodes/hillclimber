# Spec: shadcn AI Elements for the output panel

## Context

The Output panel is a flat, line-oriented dump: every opencode event becomes one
`.log-line` in a `column-reverse` scroller. It works, but it does not read like a modern
agent UI. Claude, T3Code and Paseo all do the same three things this panel does not:

- group contiguous tool activity under one collapsible trace,
- render agent prose as a message rather than a log line,
- carry a status on each step (running, done, failed).

shadcn's [AI Elements](https://ui.shadcn.com/docs/ai-elements) are the reference for the
shapes. **They cannot be installed**: each element is a copied React + Tailwind composition
built on the new
[`Trace`](https://ui.shadcn.com/docs/components/base/trace) primitive, with no package to
add. What transfers is the composition — `Trace` → `TraceLine` / `TraceCollapsible` /
`TraceIcon` / `TraceTitle`, `Tool` and `Terminal` as Trace + collapsible + code block,
`Message` for prose, `Marker` for notes. T3Code
([apps/web/src/components/chat](https://github.com/pingdotgg/t3code/tree/main/apps/web/src/components/chat))
is the UX reference: grouped work rows, dense mono labels, collapsible detail, no wasted
vertical space.

This repo already has the seam. `ClimberWeb/Services/LogFormatter.cs` classifies opencode
events into `LogKind.Text`, `Tool`, `Subagent`, `StepStart`, `StepFinish`, `Error` and
`Plain`, with the tool name, a one-line input summary and the output populated.
`Home.razor` renders those as flat lines. The missing pieces are grouping, tool status and
a reusable presentation layer.

## Decisions (locked with the user)

1. **Styling route: Razor components on the existing tokens.** No Tailwind, no React
   islands, no new build step. Port the patterns into `ClimberWeb/Components/Shared/Ai/`
   and style them from the CSS custom properties in `app.css`.
2. **First slice: traces + messages, grouped.** Contiguous tool/subagent calls become one
   collapsible trace group; prose becomes message blocks; plain/error/step lines keep a
   safe treatment. The Output panel only.
3. **Fidelity: blend.** Sans prose at a comfortable line height; mono trace rows at a
   tighter scale; grouped activity with a count/summary; failures open by default,
   successes collapsed.
4. **Reuse: an `Ai` layer + mapping doc.** One component folder, one `ai.css` token/style
   layer, and `docs/ai-elements.md` mapping every shadcn element to its Razor counterpart,
   with the not-yet-built ones marked "later" and the data they would need.
5. **Hillclimb: decide after the screenshot.** Build the slice once, look at it, then decide
   whether trace coverage becomes a measurable hillclimb objective. Not part of this slice.

## Goal

The Output panel reads as an agent transcript: prose messages, grouped tool traces that
expand on demand, visible status on each step, and raw/plain output unchanged when the
stream is not recognizable opencode JSON. The pieces are reusable for the next shadcn
pattern without redesigning the panel again.

## Approach

### 1. Data: carry tool status

`LogEntry` (a positional record) gains an optional `StepStatus Status`:

```csharp
public enum StepStatus { None, Running, Failed }

public sealed record LogEntry(
    string Raw, string Timestamp, LogKind Kind, string Text,
    string? ToolInput = null, string? ToolOutput = null,
    StepStatus Status = StepStatus.None);
```

`LogFormatter.ToolEntry` reads `part.state.status`:

| opencode `state.status` | `StepStatus` |
| --- | --- |
| `running`, `pending`, `in_progress` | `Running` |
| `error`, `failed` | `Failed` |
| `completed`, `success`, missing, anything else | `None` |

When a failed tool carries `state.error` but no `state.output`, use the error text for
`ToolOutput` so the failure has something to expand. The same status applies to a
`subagent` call. Nothing else in the formatter changes. `Plain`, `Text`, `StepStart`,
`StepFinish` and `Error` keep their current shape.

No `messageID` capture in this slice: grouping by contiguous activity gives the same
reading order without changing the formatter's contract.

### 2. Pure grouping: `LogGrouper`

New `ClimberWeb/Services/LogGrouper.cs`, a pure static function over an ordered
`IReadOnlyList<LogEntry>` returning `IReadOnlyList<OutputItem>`:

```csharp
public enum OutputItemKind { Message, TraceGroup, Marker, Error, Plain }

public sealed record OutputItem(
    OutputItemKind Kind,
    IReadOnlyList<LogEntry> Entries);
```

Rules, applied in order:

1. Consecutive `Text` entries merge into one `Message`.
2. Consecutive `Tool` and `Subagent` entries merge into one `TraceGroup`.
3. `StepStart` breaks the current group and is **not rendered** (it carries no user-facing
   information).
4. `StepFinish` breaks the current group and becomes a `Marker` (its existing cost/token
   summary is useful telemetry).
5. `Error` breaks the current group and becomes an `Error` item.
6. `Plain` breaks the current group and becomes a `Plain` item, verbatim.

Chronological order is preserved. The page reverses the item list at render time; see
"Page wiring".

### 3. Presentation: `ToolPresentation`

New `ClimberWeb/Services/ToolPresentation.cs`, pure and unit-testable, mapping a `LogEntry`
to the primitives the `Ai` components understand:

```csharp
public sealed record TraceView(
    string Icon,        // terminal | file | pencil | search | globe | subagent | gear
    string Title,       // "Ran dotnet test" / "Failed to run …" / "Read src/Foo.cs"
    string? Description,
    StepStatus Status,  // one shared status vocabulary for parsing and presentation
    string? Detail);    // expanded content, null means no <details>
```

Title rules:

| Tool | Title | Detail |
| --- | --- | --- |
| `shell`, `bash` | `Ran <command>` | output |
| `read` | `Read <path>` | output |
| `edit`, `write`, `patch` | `Edited <path>` | output |
| `glob`, `grep`, `list` | `Searched <pattern>` | output |
| `webfetch` | `Fetched <url>` | output |
| `subagent` (primary tab) | `<agent> — see its tab`, prefixed `Running:`/`Failed:` | none |
| `subagent` (its tab) | `<agent> subagent`, same prefix | prompt as description, result as detail |
| anything else | `Ran <tool>` | output; the input summary becomes the description |

Status changes the verb: `Running <…>` for `Running` and `Failed to <verb> <…>` for `Failed`, so a
failed read says "Failed to read src/Foo.cs" rather than the generic "Failed to run …". A
known input key (`command`, `path`/`filePath`, `pattern`, `url`) becomes the title; the
remaining input summary becomes the description only when it adds information. Long titles
are truncated to 80 characters with `…`; the full value lives in the expanded detail for
tools whose output is present.

### 4. Razor components: `Components/Shared/Ai/`

Primitive APIs, independent of `LogEntry` so the layer is reusable:

```razor
<AiTraceGroup Summary="4 steps">
    <AiTraceLine Icon="terminal" Title="Ran dotnet test"
                 Status="StepStatus.None" Detail="@output" />
    <AiTraceLine Icon="pencil" Title="Failed to edit src/Foo.cs"
                 Status="StepStatus.Failed" Detail="@error" />
</AiTraceGroup>

<AiMessage Text="@prose" />
<AiMarker Text="step finish · stop · $0.0002 · in 221 / out 116" />
<AiMarker Text="boom" Tone="MarkerTone.Error" />
```

- `AiTraceGroup` — a left-railed container with a small summary line. Not collapsible in
  this slice (one level of `<details>` is enough; group-level collapse is a follow-up).
- `AiTraceLine` — icon + title + optional description + status. Renders a native
  `<details>`/`<summary>` when `Detail` is non-null, and a plain row otherwise. `Status`
  drives a running shimmer and the red failure title. `Status == Failed` sets `open`;
  every other state leaves `open` absent so native toggling survives Blazor re-renders.
- `AiMessage` — prose block, `white-space: pre-wrap`, no bubble, no role label.
- `AiCodeBlock` — mono, wrapped, `max-height`, scrollable; used as the detail body.
- `AiMarker` — dim hairline note; error tone for `LogKind.Error`.
- `AiIcon` — seven inline 16px SVGs (terminal, file, pencil, search, globe, subagent,
  gear) plus a chevron. No icon package.

All components take primitives (`string`, enums, `RenderFragment`), never `LogEntry`.
`Home.razor` contains the small adapter from `LogEntry` to these props via
`ToolPresentation`.

### 5. Page wiring

- `RebuildEntries()` stores entries chronologically:
  `ActiveLogs.Select(_logFormatter.Format)` (today it reverses for the scroller).
- `VisibleEntries` filters chronologically, as today.
- `VisibleItems` = `LogGrouper.Group(VisibleEntries)` reversed for the `column-reverse`
  container. Reversing the top-level items puts the newest group at the bottom while rows
  inside a group stay chronological. A merged `Message` joins its entries with a blank
  line. The "awaiting output…" check uses `VisibleEntries.Count == 0`.
- Render `Message`, `TraceGroup`, `Marker`, `Error` and `Plain` items. Every item gets a
  `log-item` wrapper class (a stable hook for the paid harness) and `@key` on
  `Entries[0].Raw` so DOM identity — and any open `<details>` — survives a re-render.
- Subagent tabs keep filtering first, then grouping. The primary tab renders a subagent as
  a single reference row; the subagent's own tab renders its prompt and result.
- Panel meta changes from `@ActiveLogs.Count lines` to `@ActiveLogs.Count events`.
- Timestamps stay on `Plain` and `Marker` rows only; traces and messages are untimestamped
  to keep the transcript quiet.

### 6. Styling: `wwwroot/ai.css`

A new global stylesheet linked in `App.razor` after `app.css`. Only existing tokens; no new
palette and no `color-mix` beyond what the app already uses. The semantic classes:

| Class | Purpose |
| --- | --- |
| `.ai-message` | sans prose, 0.9rem, 1.65 line height, 72ch max |
| `.ai-trace-group` | 2px left rail, 0.65rem indent, tight row gap |
| `.ai-trace-group-head` | mono 0.68rem, dim, count/summary |
| `.ai-trace-line` | row: icon, title, description, status; 0.22rem vertical padding |
| `.ai-trace-title` | mono 0.76rem, ink; red on failure |
| `.ai-trace-desc` | mono 0.72rem, dim, single-line ellipsis |
| `.ai-trace-detail` | indented detail body |
| `.ai-code` | mono 0.72rem, wrapped, 16rem max, surface + hairline border |
| `.ai-marker` | mono 0.7rem, dim, hairline above |
| `.ai-status-running` | subtle shimmer, disabled by the existing reduced-motion rule |
| `.ai-chevron` | rotates on `[open]` |

`summary` gets the app's standard focus ring. `Home.razor.css` keeps the `.log` container
and the `.log-line` classes for `Plain` lines; the old `.log-tool-*`, `.log-subagent-*` and
`.log-event.log-error` rules move to `ai.css` or disappear with the markup they styled.

### 7. Docs: `docs/ai-elements.md`

- One paragraph: patterns, not a package; this repo ports the composition.
- A table of every shadcn AI Element / base component → Razor counterpart → status
  (`now` / `later`) → data it would need. `now` covers Trace, Trace Group, Tool, Terminal,
  Message, Marker, Code Block, Subagent, Chat container. `later` covers Reasoning, Chain of
  Thought, Plan, Task, Queue, Test Results, File Tree, Diff, Stack Trace, Prompt Input,
  Model Selector, Confirmation, Sources, Inline Citation, Context, Checkpoint, Attachment,
  Suggestion.
- The token list the layer is allowed to use.
- A short "add the next element" recipe: port the shape, add `.ai-*` styles, put any data
  transformation in a pure service with tests, keep the `Plain` fallback.

## What it should do

1. A run's prose renders as one or more message blocks, not as flat log lines.
2. Contiguous tool/subagent calls render as one trace group with a small step count.
3. Each tool call is exactly one trace row; known tools get a specific verb and icon; the
   raw `tool` name is never the whole title.
4. A tool with output expands to a wrapped, height-capped code block; a tool without output
   is a single non-expandable row.
5. A failed tool is red, says "Failed to <verb> …", and starts expanded; a successful tool
   starts collapsed; a running tool shows a running state.
6. `StepFinish` renders as a dim marker with the existing cost/token summary; `StepStart`
   is invisible.
7. An opencode `error` event renders as a red marker with the full message.
8. Anything that is not a recognized opencode event still renders verbatim, with the
   current `LineClass` coloring. A failed or non-JSON run is never blank.
9. Subagent tabs still show only that agent's calls; the primary tab still points at the
   delegated work.
10. Expanding an older row does not reset when new output streams in.
11. Keyboard and screen-reader users can toggle a trace with `Enter`/`Space`, and status is
    conveyed in text, not color alone.

## Out of scope

- The composer, model selector, attachments or any configure-panel redesign.
- Markdown rendering (bold, lists, fenced code, links). Prose is plain text with preserved
  line breaks in this slice; a renderer plus sanitisation is its own decision.
- Diff, test-results, file-tree, plan, reasoning, sources, context meter, checkpoint,
  stack-trace or copy-to-clipboard.
- Group-level collapse (nested `<details>`), virtualization, or a scroll-to-bottom script.
- bUnit or any component-test project. The pure services carry the tests.
- Changing the run/stream data or adding a paid run to the verification.
- The polish audit's score-floor and blind spots (backlog item 05). Note the score after
  the change; do not fix the harness here.

## How we know it is done

- **Unit tests:** `dotnet test` is green with new `LogGrouperTests`, `ToolPresentationTests`
  and formatter status cases. Coverage: message merge, tool/subagent grouping, group breaks
  on text/step/error/plain, chronological order, empty input, every title/icon branch,
  subagent primary vs tab, status mapping, failed-tool error fallback. `Home.razor` still
  has no component test; the page wiring is covered by the browser check and review, and
  the spec says so plainly.
- **Free browser harnesses:** `theme-nav.mjs`, `providers.mjs`, `security.mjs` pass with no
  console errors. `polish.mjs` runs if no run is active; record the score before and after.
- **Visual check:** start `ClimberWeb` with a temporary seed (one throwaway file plus one
  hook in `ActiveLogs`, both removed before commit) that produces: a plain line, two prose
  paragraphs, four tool calls (shell, read, edit, one failed), a subagent, a step finish and
  an error. A temporary `%TEMP%\climb-ui\ai-output-check.mjs` (outside the repo, no tokens)
  asserts: `.ai-trace-group` and `.ai-message` are present; each tool entry is exactly one
  `.ai-trace-line`; no trace contains raw JSON; the failed row's `<details>` is open; a
  successful row is closed, opens on click, and stays open after a 1s wait; a plain line is
  unchanged; the primary tab says "see its tab"; the subagent tab shows the result; no
  horizontal overflow at 375px; zero console errors.
- **Screenshots:** 900×1200 and 375×812, light and dark. Review the transcript read, the
  trace density, the collapsed/expanded states and the failure color.
- **Cleanup:** the seed is removed, `git status` shows only the intended files, and the app
  is stopped.
- **`surfaces.mjs` is NOT run** (paid). Its `.log-line` count/text selectors are updated to
  the new `log-item` hook in the same change, and the report says plainly that the edit was
  never executed.
- **Hillclimb:** after the screenshot, decide whether "one trace per tool call, zero flat
  tool lines" becomes the measurable gate. If yes, that is a follow-up run, not this slice.

## Risks to watch

- **Native `<details>` across re-renders.** The plan relies on `@key` and omitting `open`
  for non-failures. The browser check explicitly opens a row, waits for a state tick and
  confirms it is still open. If it resets, switch to a `HashSet<string>` of open raws.
- **`column-reverse` anchor.** Expanding an older group can jump the scroll position. Check
  it in the browser; a normal column plus scroll-to-bottom would add JS and is a last resort.
- **Real event shapes.** The status mapping assumes `state.status` values seen in the
  formatter fixtures. Unknown values degrade to `None` ("Ran …"), never to an error. A
  single short Surplus-only run is optional and not required by this slice.
- **The polish audit.** The panel markup changes under `.panel`; `polish.mjs` may score
  differently. Record the number, do not chase it here.
- **Raw unknown events.** Reasoning or other unhandled event types remain raw JSON lines.
  That is the correct fallback until their element is ported; do not hide them.

## Evidence (2026-11-15)

**Unit tests.** `dotnet test --nologo` after the seed was removed:
`Passed! - Failed: 0, Passed: 306, Skipped: 0, Total: 306, Duration: 361 ms`.
After the review fixes the suite is **311 passing, 0 failed, 0 skipped** (final run 315 ms),
and `scripts/status.ps1` confirms the same counts with 63.11% line coverage.
`dotnet build ClimberWeb --nologo`: 0 warnings, 0 errors.

**Seeded browser check.** App started with `CLIMBER_SEED=1` (throwaway
`ClimberWeb/SeedSampleLogs.cs` plus one `ActiveLogs` hook, both deleted). Temporary
`%TEMP%\climb-ui\transcript-check.mjs`, 24 checks, **all passed**:

- 2 `.ai-message`, 3 `.ai-trace-group`, 8 `.ai-trace-line` for 8 tool/subagent entries;
  panel meta reads `15 events`.
- No trace row contains raw JSON; titles read `Ran Get-ChildItem …`, `Read …`, `Searched
  RebuildEntries`, `Edited …`, `Failed to run dotnet build …`.
- Step-finish marker, one red error marker, the exact plain line, and the running row all
  render.
- Failed `<details>` open by default; a success starts closed, opens on click, and stays
  open across a real Blazor re-render (method tab toggled `run` → `hillclimb` → `run`),
  which is stronger than the 1s wait the plan asked for.
- Primary tab shows `plan-architect — see its tab`; the subagent tab shows one trace with
  its prompt and unwrapped result.
- No horizontal overflow at 375px in either theme; zero console errors.

**Review-fix check.** Second seeded run (`%TEMP%\climb-ui\review-fixes-check.mjs`, 6 checks,
all passed) plus a 375px screenshot: the error marker starts `12:00:15 · `, the failed
subagent reads `Failed: plan-architect — see its tab` / `Failed: plan-architect subagent`,
its detail is open with the error text, and no overflow or console errors at 375px.

**Screenshots** (`%TEMP%\climb-ui-shots`): `transcript-900-{light,dark}.png`,
`transcript-375-{light,dark}.png`, plus `transcript-expanded-*` with the scroller opened
only for review. Reviewed: grouped activity reads as one block, mono rows stay dense, the
failure is red and pre-expanded, prose is sans and untimestamped, markers are quiet.
`review-fixes-900-{light,dark}.png` and `review-fixes-375-light.png` cover the follow-up.

**Free harnesses** (app running, no seed):

- `theme-nav.mjs`: 3 pages crawled, 0 failures, no console errors.
- `security.mjs`: 10 checks, 0 failures.
- `providers.mjs`: 15 checks, 0 failures.
- `polish.mjs`: 56 cells / 560 checks, UPS **100** (no before-score was taken in this
  session).

**Not run.** `scripts/ui/surfaces.mjs` is paid (drives real `opencode` runs) and was not
executed; only its `.log-line` → `.log-item` selectors were updated, unverified by any run.

**Cleanup.** Seed and hook removed; `git status` lists only the intended files.

## Post-review decisions

`/code-review 6149542` raised two judgement calls; both are settled here.

1. **Failure wording — keep it per-verb.** The review noted the implementation says
   "Failed to read src/Foo.cs" where the spec's template said "Failed to run …". The
   per-verb form is clearer and grammatically right; the rule above was amended and
   `ToolPresentationTests` now covers read and edit failures as well as shell.
2. **One status vocabulary.** `ToolStatus` (parser) and `TraceStatus` (presentation) held the
   same three values with a pass-through mapper. They are now a single `StepStatus` in
   `ClimberWeb/Services/StepStatus.cs`, used by `LogEntry`, `TraceView` and `AiTraceLine`.

Four factual findings were fixed in the same pass:

- Error markers keep their timestamp: the error row uses `MarkerText`, so it reads
  `12:00:15 · Provider …` like a step-finish marker.
- A running or failed subagent says so in text on both tabs: `Running: plan-architect — see
  its tab`, `Failed: plan-architect subagent`. The state leads the title so a narrow row can
  clip the tail without hiding it, and `.ai-trace-desc` is capped at 40% so a long prompt
  cannot squeeze the title.
- An object-shaped `state.error` falls back to its raw JSON, so a failed tool still has an
  expandable body.
- `README.md` and the committed `status/coverage.json` were regenerated to 311 passing and
  63.11% line coverage.

## Fixed point for review

Session start HEAD is `6149542`. Review the implementation against this spec with
`/code-review 6149542`.