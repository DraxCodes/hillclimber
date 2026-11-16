# AI Elements and shadcn in this app

Status: **rewritten 2026-11-16**. The previous version described the pre-pivot
`ai.css` layer and named files that no longer exist (`Components/Shared/Ai/`,
`AiTurn`, `AiMarker`, `AiCodeBlock`, `AiMessage`, `AiMarkdown`, `AiTerminalCard`,
`AiTraceLine`). Evidence and per-surface findings:
[`../.scratch/ui-provenance-audit.md`](../.scratch/ui-provenance-audit.md).

[shadcn AI Elements](https://ui.shadcn.com/docs/ai-elements) are **patterns, not a
package**. Each one is a composition of shadcn/ui parts that you copy into a
project. In a Blazor app you port the *shape* - markup, classes, ARIA and states -
never the TSX.

The rules that govern a port are in [`ui-contract.md`](ui-contract.md). This file
records what exists and where.

## How the layer works

- **Styling.** Tailwind 4. `wwwroot/tw.input.css` maps the shadcn token names onto
  the palette in `wwwroot/app.css` (`@theme inline`), then imports the Tailwind
  layers. `wwwroot/tw.css` is the generated output and is committed, so
  `dotnet run` needs no Node. Rebuild with `npm run css` from `ClimberWeb/`.
- **Preflight is deliberately off.** `app.css` already normalises the document and
  the legacy screens still depend on its defaults.
- **Dark mode** is an attribute (`[data-theme="dark"]`), not a class and not the OS
  preference alone, via `@custom-variant dark`.
- **Ports live in two places.** AI surfaces in `ClimberWeb/Components/Ui/Ai/`,
  general components in `ClimberWeb/Components/Ui/`.
- **Open state lives in C#**, not the DOM. A re-render must not lose a reader's
  choice, so every disclosure keeps a `bool? _chosen` and falls back to a rule.

Registry source for any port:
`https://ui.shadcn.com/r/styles/base-rhea/<name>.json`.

## Base primitives ported (`Ui/Ai/`)

| Element | File | Notes |
|---|---|---|
| Trace | `Trace.razor` | missing the registry's child-focus block (debt B1) |
| TraceLine | `TraceLine.razor` | exact |
| TraceIcon | `TraceIcon.razor` | missing second-icon tightening (debt B2) |
| TraceTitle | `TraceTitle.razor` | exact, plus deliberate `Title` / `Wrap` |
| TraceDescription | `TraceDescription.razor` | exact |
| TraceCollapsible | `TraceCollapsible.razor` | height animates 0 to auto (`interpolate-size` + `@starting-style`); close unmounts |
| Message | `Message.razor` | `Align="end"` is the person, default is the agent |
| MessageContent | `MessageContent.razor` | exact |
| Bubble | `Bubble.razor` | wrapper + content, `muted` / `ghost` / `outline` |
| Code Block | `CodeBlock.razor` | `Size="sm"` for tool output; the Tool composition caps it, the port does not |
| Inline Citation | `InlineCitation.razor` | CSS-only card, no floating library |
| Marker | `Marker.razor` (+ `MarkerIcon`, `MarkerContent`) | errors, notes and footers; `separator` and `border` unused for now |
| Markdown | `Markdown.razor` | renders `MarkdownParser` records, never HTML |
| Prompt Input | `PromptInput.razor` | tools row, textarea, send/stop |

Shared presentation types are in `Ai/AiTypes.cs`: `PlanStep`, `SourceLink`.

## Patterns composed (`Ui/Ai/`)

| Pattern | File | Composition |
|---|---|---|
| Tool, Terminal | `AiTool.razor` | `Trace` + `TraceCollapsible` + `CodeBlock`; a failure opens its output |
| Reasoning | `AiReasoning.razor` | `Trace` + `TraceCollapsible` + `Markdown`; opens while streaming |
| Plan | `AiPlan.razor` | `Trace` of `TraceLine` steps with a done-of-total description |
| Task | `AiTask.razor` | nested `Trace` of steps; receives no steps today (debt D2) |
| Sources | `AiSources.razor` | `Trace` of links, newest first |

**Chain of Thought** is not a file: the turn chrome is composed inline in
`Pages/Home.razor` (`TraceCollapsible` around a summary line, with the turn's rows
as the body). That is the documented composition, not a substitute for it.

## General primitives ported (`Ui/`)

Alert, Badge, Button, Card (and Header, Title, Description, Content, Footer),
Empty, PagePanel, Popover, Separator, Skeleton, Spinner, Table (and Body, Cell,
Head, Header, Row), Tooltip, plus the `UiIcon` icon set and the `UiClass` joiner.

## Not built

| Element | Why | Debt |
|---|---|---|
| Message Scroller | the feed is a hand-rolled `column-reverse` scroller | C2 |
| Suggestion | the empty state's chips are bespoke | C3 |
| Tabs | `.methodbar` is a hand-rolled tablist | C4 |
| Toggle Group | the output strip is a hand-rolled group of `aria-pressed` buttons | - |
| Dialog, Sheet | the folder picker and mobile rail are bespoke `role="dialog"` | C5 |
| Input, Input Group | folder, iterations and PIN are raw `<input>` | C6 |
| Model Selector | one configured model per run; read-only by design (exception) | EXCEPTION 2 |
| Queue, Context, Checkpoint, Confirmation, File Tree, Stack Trace, Test Results, Attachment, Questionnaire | the events or metadata are not emitted by `opencode run`, or the surface does not exist | - |

The working indicator has no element in the catalogue; a `TraceLine`-shaped row
with `role="status"` is the recorded exception (EXCEPTION 3).

## Data services the ports read

| Service | Role |
|---|---|
| `LogFormatter` | one raw opencode event to a `LogEntry` |
| `LogGrouper` | entries grouped into transcript rows and turns |
| `TurnPresentation` | a turn's summary, status, default open state and footer |
| `ToolPresentation` | a tool event's title, icon, description, detail, command, exit code |
| `OutputActivity` | the header phrase and the delegate-tab pulse |
| `StepScrollMath` | where the feed must scroll after a disclosure opens |
| `MarkdownParser` | the markdown subset, as records |
| `SpendFormat` | tokens and cost |
| `ProviderRegistry`, `Catalogue` | provider state, model lookup, pricing |

All of these are pure and unit-tested; keep them independent of Razor.

## Adding the next element

1. Fetch the registry item JSON and read the classes, ARIA and states. For a
   pattern, read the AI Elements page as well; the pattern is a composition even
   when the page shows one file.
2. Port the primitives it needs into `Ui/Ai/` first. A pattern never invents a
   row shape; it composes `Trace`, `Message` or `Code Block`.
3. Keep open/closed state in C# and let the component render from it. Do not let a
   re-render reset a reader's choice.
4. If the element needs transformed data, add a pure service next to
   `LogGrouper` and unit-test it. Do not parse inside a component.
5. State the surface in [`ui-contract.md`](ui-contract.md) and clear its debt ID.
   A surface that is still in the debt register cannot be cited as done.
6. Verify with `dotnet test`, the free browser harnesses
   (`node scripts/ui/polish.mjs`) and a seeded screenshot. A paid run
   (`surfaces.mjs`) is only warranted if the change alters what streams, not how
   it renders.

## Preserve the fallback

An event the parser does not recognise must still render verbatim. A failed run
that renders as nothing is worse than an ugly row.