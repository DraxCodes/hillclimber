# UI contract

Status: **in force (2026-11-16)**. Evidence and per-surface findings:
`.scratch/ui-provenance-audit.md`.

## Why this exists

The first shadcn climb produced seven style-only iterations in one hour. Nothing
in the loop named the element a surface was supposed to be, so the gate could only
score spacing, and spacing was all that changed. This contract is the missing
input: it says what each surface is, what it owes, and when a deviation is a
decision rather than drift.

The contract is checkable. Every rule below names how it is checked and what
counts as a failure.

## Rules

### R1 - Provenance

Every visible AI surface is the documented AI Element, or a composition of
`Trace` / `Message` / `Code Block`, or a named exception in the register.
Every non-AI surface is a shadcn base component, or a named exception.

**Checked by:** the provenance probe in
[`docs/agents/hillclimb-rubric.md`](agents/hillclimb-rubric.md).
**Fails when:** a bespoke row, panel or chip stands where a documented element
exists, and no exception names it.

Composing a pattern from `Trace` is provenance, not drift - the docs say every AI
pattern "is a composition of `Trace` parts". Inventing a *new row shape* is drift.

### R2 - Composition

A pattern is built from its parts, not imitated with a lookalike:

- A turn is `Trace` lines inside `Message`, not a bespoke card.
- The thread is `Message` turns inside a scroller, not a list of cards.
- A summary expands into its steps; a failure opens to its output; a note or a
  divider is a `Marker`.

**Checked by:** the composition probe.
**Fails when:** the visual result is a lookalike whose parts, ARIA or states
differ from the documented composition.

### R3 - States

Resting appearance is not evidence. Each interactive or streaming element owes:
closed, open, running, complete, failed, disabled - both themes, 375 and 900.

**Checked by:** the state-fidelity probe.
**Fails when:** a component has one state, a dead trigger whose `aria-expanded`
never changes, or a state that exists in the registry and not here.

### R4 - Width and alignment

One width model per column. A width is either the documented content-width
(`w-fit`, as `TraceLine` defines it) or a deliberate full-width row. Mixing both
in one column is a flaw unless an exception names the row.

**Checked by:** the three-second and width probes, plus the polish harness.
**Fails when:** sibling rows in one column resolve to different width models, or
a description's right edge moves because a title's length changed - unless that
is the documented behaviour and the column was designed for it.

`TraceLine` is `w-fit` by design, so right-edge alignment of descriptions is not
an official feature. If a column needs a shared right edge, that is a deviation
and it gets written down.

### R5 - Tokens

Ports use only the shadcn semantic tokens (`bg-background`, `text-muted-foreground`,
`border-border`, `bg-muted`, `ring-ring`, `text-destructive`, ...), which map onto
the palette in [`app.css`](../ClimberWeb/wwwroot/app.css). No hard-coded hex, no
new palette value inside a port, no raw `--ink-*` variable in a `Ui/` component.

**Checked by:** the token probe.
**Fails when:** a port introduces a colour outside the documented mapping, or a
token exists in one theme and not the other.

### R6 - Exceptions

A deviation is allowed only if it is **recorded in the register below** with its
cost. Unnamed drift is a flaw. A recorded exception is a decision and is scored on
its stated cost, not on the rule it breaks.

### R7 - Art direction

The screen is arranged well, not merely assembled correctly: three levels of
hierarchy, a 60-75ch measure for prose, 8px inside a group and 24px+ between
groups, three type sizes plus one micro, one accent with one job, and mono only
where the reader is reading data.

**Checked by:** probe 13 against
[`ui-art-direction.md`](ui-art-direction.md).
**Fails when:** every element is the right one and every state exists, yet the
answer, the machinery and the metadata all compete at the same weight.

Passing R1-R6 caps the score at 8. A 9 or a 10 requires R7.

## Surface map

`port` = a Razor port of the registry component. `composition` = built from
ported primitives. `bespoke` = no documented equivalent yet. Owner is the file
that decides the surface.

| Surface | Documented element | How | Owner |
|---|---|---|---|
| Chat shell | Chat | composition | `Pages/Home.razor` |
| Thread scroller | Message Scroller | **EXCEPTION 1** | `Pages/Home.razor`, `wwwroot/tw.input.css` |
| Output strip | Toggle Group | bespoke, **decision below** | `Pages/Home.razor` |
| Message rows | Message | composition | `Ui/Ai/Message.razor` |
| User prompt | Message + Bubble | composition | `Ui/Ai/Bubble.razor` |
| Turn / steps | Chain of Thought | composition | `Ui/Ai/TraceCollapsible.razor` |
| Plan | Plan | port | `Ui/Ai/AiPlan.razor` |
| Task / Subagent | Task / Subagent | port, **debt D2** | `Ui/Ai/AiTask.razor` |
| Tool / Terminal | Tool / Terminal | port | `Ui/Ai/AiTool.razor` |
| Reasoning | Reasoning | port | `Ui/Ai/AiReasoning.razor` |
| Sources | Sources | port | `Ui/Ai/AiSources.razor` |
| Errors / notes / footers | Marker | port | `Ui/Ai/Marker.razor`, `Pages/Home.razor` |
| Unknown event | Tool | composition | `Services/ToolPresentation.cs` |
| Working indicator | - (no element) | **EXCEPTION 3** | `Services/OutputActivity.cs` |
| Jump to latest | `MessageScrollerButton` | bespoke, **debt C2** | `App.razor` |
| Empty state | Empty | port | `Ui/Empty.razor` |
| Prompt suggestions | Suggestion | bespoke, **debt C3** | `Pages/Home.razor` |
| Composer | Prompt Input | port, **debt C6** | `Ui/Ai/PromptInput.razor` |
| Method / provider pick | Tabs / Toggle Group | bespoke, **debt C4** | `Pages/Home.razor` |
| Model chip | Model Selector | **EXCEPTION 2** | `Ui/Ai/PromptInput.razor` |

Debt IDs refer to the register in the audit (section 9). A debt ID is not an
exception: it is a known gap scheduled for a phase, and it does not license new
drift on the same surface.

### Phase 2a decisions

Recorded so a later reader does not mistake each of these for drift.

- **Marker** uses its `default` variant for errors, standalone notes and turn
  footers. The `separator` and `border` variants ship as port parity and are
  unused: where a divider belongs is an art-direction decision, not a
  provenance one.
- **Plan is run-scoped.** One Plan lists every agent the run handed work to
  (first-seen order, latest state) and renders once, above the transcript, where
  `column-reverse` reads it directly under the prompt. While a Plan exists the
  per-call `- see its tab` rows are not drawn: the entry stays in its turn, so the
  turn still counts the work it did, but the Plan is the record of who ran and in
  what state. A run with a single delegate has no Plan, so that row is drawn.
- **A collapsed turn shows its summary alone.** Its cost footer lives in the body:
  printed beside the label it took the whole row and squeezed the label to `W…`.
- **Code Block height cap** belongs to the Tool composition (`max-h-72`, 18rem -
  the same number `app.css` gives a fenced block in prose), not to the port: the
  registry's Code Block has no max height. Its focus ring is `focus-within` on
  the wrapper because the registry's nested `has-[>[...]:focus-visible]`
  arbitrary variant does not generate in this Tailwind build.
- **TraceCollapsible** animates height 0 to auto over 200ms through
  `interpolate-size` and `@starting-style`, and opens instantly under
  `prefers-reduced-motion`. Closing unmounts the content, so there is no exit
  motion and a long run keeps no closed content in the DOM.
- **Focus rings use `ring-ring`, not `ring-ring/30`.** The registry's `/30`
  assumes an opaque `--ring`; this palette's is already 72% opaque, so `/30`
  measured about 1.4:1 and became the faintest indicator in the app. Both new
  rings (fold trigger, code block wrapper) now match the weight of the app's
  existing `focus-ring` outline.
- **Sources** count an MCP tool when its input names an http(s) URL, which makes
  the documented "Web or MCP targets" claim true. An MCP call with no URL adds
  nothing.

### Phase 2b decisions

Recorded so a later reader does not mistake each of these for drift.

- **The user prompt is prose on a bordered bubble.** It renders the `Bubble`
  `muted` variant with `font-sans`, `border-border` and `max-w-[68ch]`. The feed
  sets `font-family: var(--font-mono)`, so a bubble that inherits it makes the
  person's sentence look like data; a hairline border separates the surface from
  `--surface-2`, which is only 8/255 lighter than `bg-muted`. The caller's classes
  beat the port's `border-transparent` and `max-w-full` through
  `UiClass.Join`'s conflict resolution (Phase 2c), not an `!` escape.
- **A tool row's argument summary is bounded** (`max-w-[12ch] sm:max-w-[34ch]`,
  `truncate`, `title`). `TraceDescription` is `shrink-0` because the registry says
  so, which is right for short meta ("3 of 4 done") and wrong for an 80-character
  `query=… · url=…`: it forced the fold body to 811px and the `overflow-hidden`
  content clipped the row at 375 and 900. The bound belongs to the composition
  (`AiTool`), not the primitive. `TraceDescription` forwards unmatched attributes,
  matching the registry's `...props`, so the full value stays available on hover.
- **An unknown event is a Tool row, never a raw log line.** `session.diff` is
  named: its title is "Changed N file(s)" and its body lists the files. Any other
  unknown type keeps its humanized title and shows its payload without the
  `[HH:mm:ss]` stamp. The stamped line is machinery, and the generic branch is what
  a future event type reaches by default.
- **The output strip states that it scrolls.** One row never wraps (a second row
  puts the underline on the wrong line), so when `scrollWidth > clientWidth` the
  strip's trailing edge is masked and the selected button is scrolled into view.
  The class is `data-scrollable`; it is off when the buttons fit.
- **A cost line wraps between clauses, not inside one.** The footer is split on
  ` · ` into no-wrap spans, and the separator travels with the clause it precedes,
  so a wrapped footer reads `· cache 5120 read / 640 write` rather than ending a
  line with a loose dot or orphaning `read`. Standalone `step_finish` markers use
  the same rendering; other markers still wrap normally.
- **Prose is sans in the empty state too.** The feed's mono default reached the
  empty-state sentence; it is now `font-sans` at the 14px the `Empty` description
  already sets.
- **The composer's footnote is one line.** The delegation note already lives in the
  agent chip's `title`; printing it again under the composer added a second 10px
  mono line that said the same thing.
- **The chat title row carries the title and nothing else.** A method badge sat
  beside it (`run` / `hillclimb`) with the same border, radius and mono caps as the
  interactive method tabs one band above, so it read as a second, dead control on
  the one row that names the transcript; it also duplicated that picker and ate
  ~40px of the title at 375. The method is stated by the tabs and, before a run,
  by the title itself ("new run" / "new hillclimb"). Its `hidden` at 375 was never
  real either: the badge's base `inline-flex` won, which is one of the
  compositions `UiClass.Join` now resolves (Phase 2c).
- **The empty-state heading stays mono while its sentence is sans.** The shell's
  heading idiom is mono and only prose is sans; the pair is deliberate, not a
  missed font. The sentence is the reading text and follows the rule; the heading
  is a label like the panel's other labels.

### Phase 2c decisions

Recorded so a later reader does not mistake each of these for drift.

- **`UiClass.Join` resolves conflicts like the registry's `cn()`.** A later class
  in the same utility family wins, and a later *broader* utility removes the
  narrower ones it covers (`p-0` over `px-4 py-2`); variants compare whole, so
  `hover:bg-accent` and `bg-primary` both survive; an unknown utility is never a
  conflict. `Components/Ui/TailwindConflict.cs` lists the families the ports
  compose and `UiClassTests` pins them. The `!` escapes are gone from the
  components (`text-destructive`, `border-border`, `max-w-[68ch]`, and
  `PagePanel`'s spacing): the caller's class now wins on its own.
- **The output strip is a labelled group of toggle buttons, not a tablist.**
  `role="tab"` promised a keyboard contract the strip never kept (no roving
  tabindex, no arrow keys), and a half-implemented tablist is worse for a screen
  reader than an honest toggle: each button carries `aria-pressed`, Tab reaches
  all of them, and the container is `role="group" aria-label="Output stream"`.
  The trailing-edge mask and the scroll-into-view are unchanged. `.methodbar`
  still declares tabs and is the remaining half of debt C4.

## Exception register

### EXCEPTION 1 - reverse transcript order

The combined feed renders newest-first (`flex-direction: column-reverse`), not
the chronological DOM that Message Scroller documents.

**Why:** the browser pins the visual bottom for free, so a streaming row never
moves the viewport, and 12 tests in `StepScrollMathTests.cs` pin the resulting
scroll behaviour.

**Cost:** reading order and `Ctrl+Home` are reversed for assistive technology
(WCAG 1.3.2 sequence). Screen readers start at the newest turn.

**Until:** the Message Scroller adoption (audit Phase 3). Any *new* scroll
feature must state which axis it assumes.

### EXCEPTION 2 - read-only model chip

The composer names the exact provider and model but offers no selector. The Model
Selector element is two menus; this app has one configured model per run.

**Cost:** the model cannot be changed from the composer. Accepted in
`.scratch/ui-pivot-shadcn/spec.md` section 4.

### EXCEPTION 3 - working indicator

No element covers "the run is doing X right now". A `TraceLine`-shaped row plus a
pulsing dot is allowed; it must sit on the same left edge as the feed's rows and
carry `role="status"`.

The shipped indicator is a header pill (`span.log-working`) at the end of the chat
title row, not a `TraceLine` row on the feed's left edge: it names the running
step and its elapsed time while the transcript stays the reader's, and the head of
the feed is where the newest row lands, so a row there would be covered by the
stream. It keeps the pulsing dot and `role="status"` and is `aria-hidden` apart
from the `aria-label`.

**Cost:** it shares a row with the chat title, so at 375 the title truncates
sooner during a live run.

## Scoring interaction

- A UI surface with an **unnamed** provenance, composition or state violation
  cannot score above **7**.
- A **recorded** exception does not cap the score. It is judged on its stated
  cost - for example, EXCEPTION 1 is not a visual flaw, so it does not block an 8.
- A **debt** item does not cap the score either, but a judge may not claim credit
  for the surface it belongs to. "Marker renders errors" cannot be true while C1
  is open.
- Passing every rule in this file is a **floor**, not a ceiling: 8 is the
  mechanical maximum. A 9 or a 10 is earned by R7 against
  [`ui-art-direction.md`](ui-art-direction.md).
- Every scorecard cites a `Contract:` line: `no violations`, or the violation and
  its surface.

## Checks

| Rule | Probe | Command |
|---|---|---|
| R1, R2 | provenance, composition | rubric probes 8 and 10 |
| R3 | state fidelity | rubric probe 9 |
| R4 | three-second, width | rubric probes 1 and 4; `node scripts/ui/polish.mjs` |
| R5 | token | rubric probe 12 |
| R6 | - | this file |
| R7 | art direction | rubric probe 13 |

Registry source for a port: `https://ui.shadcn.com/r/styles/base-rhea/<name>.json`.
Docs: `https://ui.shadcn.com/docs/ai-elements` and
`https://ui.shadcn.com/docs/components/base/<name>`.