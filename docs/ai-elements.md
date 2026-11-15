# AI Elements in this app

[shadcn AI Elements](https://ui.shadcn.com/docs/ai-elements) are **patterns, not a package**.
Each element is a composition of shadcn/ui components (React + Tailwind, built on Radix,
Base UI or React Aria) that you copy into a project. Nothing from the catalogue can be
installed into a Blazor app; what transfers is the composition and the behaviour.

This app ports those compositions into Razor, with hand-written CSS on the existing design
tokens. The reusable layer lives in `ClimberWeb/Components/Shared/Ai/` and
`ClimberWeb/wwwroot/ai.css`. The Output panel is the first consumer.

## Element map

`now` means the shape is implemented here. `later` means the pattern is understood and the
data it needs is named, but it has not been built.

| AI Element / base component | Razor counterpart | Status | Data it needs |
| --- | --- | --- | --- |
| [Trace](https://ui.shadcn.com/docs/components/base/trace) | `AiTraceLine` | now | one `LogEntry` (tool/subagent) |
| [Chain of Thought](https://ui.shadcn.com/docs/ai-elements/base/chain-of-thought) | `AiTurn` + `TurnPresentation` | now | one step's reasoning, tool rows and `step_finish` footer |
| [Tool](https://ui.shadcn.com/docs/ai-elements/base/tool) | `AiTraceLine` + `AiCodeBlock` | now | tool name, input summary, output, status |
| [Terminal](https://ui.shadcn.com/docs/ai-elements/base/terminal) | `AiTerminalCard` + `AiCodeBlock` | now | `shell`/`bash` title, full command, exit code and output; copy via `data-copy` |
| [Message](https://ui.shadcn.com/docs/ai-elements/base/message) | `AiMessage` | now | consecutive `text` events |
| [Chat](https://ui.shadcn.com/docs/ai-elements/base/chat) | `.log` panel + `LogGrouper` | now | the whole feed |
| [Subagent](https://ui.shadcn.com/docs/ai-elements/base/subagent) | `AiTraceLine` (subagent variant) | now | agent, prompt, result |
| [Code Block](https://ui.shadcn.com/docs/components/base/code-block) | `AiCodeBlock` | now | tool output |
| [Marker](https://ui.shadcn.com/docs/components/base/marker) | `AiMarker` | now | a footer with no activity, `error` |
| [Reasoning](https://ui.shadcn.com/docs/ai-elements/base/reasoning) | `AiReasoning` | now | `reasoning` events, collapsed unless opened |
| [Plan](https://ui.shadcn.com/docs/ai-elements/base/plan) | — | later | plan events; not parsed |
| [Task](https://ui.shadcn.com/docs/ai-elements/base/task) | — | later | task/todo events; not parsed |
| [Queue](https://ui.shadcn.com/docs/ai-elements/base/queue) | — | later | the agent's todo list; not parsed |
| [Test Results](https://ui.shadcn.com/docs/ai-elements/base/test-results) | — | later | per-suite results; `TestResultParser` only gives pass/fail totals |
| [File Tree](https://ui.shadcn.com/docs/ai-elements/base/file-tree) | — | later | changed-file list; not parsed |
| Diff | — | later | git diff output; not parsed |
| [Stack Trace](https://ui.shadcn.com/docs/ai-elements/base/stack-trace) | — | later | tool error text is captured; frame parsing is not |
| [Sources](https://ui.shadcn.com/docs/ai-elements/base/sources) | — | later | source metadata; not emitted by `opencode run` |
| [Inline Citation](https://ui.shadcn.com/docs/ai-elements/base/inline-citation) | — | later | source metadata |
| [Context](https://ui.shadcn.com/docs/ai-elements/base/context) | — | later | step token usage exists; the model window size is not known |
| [Checkpoint](https://ui.shadcn.com/docs/ai-elements/base/checkpoint) | — | later | checkpoint events; none |
| [Confirmation](https://ui.shadcn.com/docs/ai-elements/base/confirmation) | existing approval notices | later | the provider-approval flow already exists; only the shape would change |
| [Prompt Input](https://ui.shadcn.com/docs/ai-elements/base/prompt-input) | `#prompt` textarea | later | a composer redesign |
| [Model Selector](https://ui.shadcn.com/docs/ai-elements/base/model-selector) | provider bar | later | a configure-panel redesign |
| [Suggestion](https://ui.shadcn.com/docs/ai-elements/base/suggestion) | — | later | suggested prompts; none |
| [Attachment](https://ui.shadcn.com/docs/components/base/attachment) | — | later | attachments; none |
| [Bubble](https://ui.shadcn.com/docs/components/base/bubble) | — | later | not needed for the current blend |
| [Questionnaire](https://ui.shadcn.com/docs/components/base/questionnaire) | — | later | agent questions; none |
| [Message Scroller](https://ui.shadcn.com/docs/components/base/message-scroller) | `.log` (`column-reverse`) | now | no virtualisation |

## Tokens

`ai.css` uses only these variables from `app.css`:

`--bg`, `--surface`, `--surface-2`, `--ink`, `--ink-2`, `--ink-3`, `--line`, `--line-2`,
`--accent`, `--accent-text`, `--green`, `--orange`, `--red`, `--focus`, `--radius-sm`,
`--radius`, `--radius-lg`, `--font-sans`, `--font-mono`.

Both themes work by construction: the dark block in `app.css` redefines the palette and
`ai.css` never hard-codes a colour. No Tailwind and no icon package; the only JavaScript is
the delegated `window.climberLog` script in `App.razor` for copying a terminal command,
closing a transcript disclosure with Escape, and revealing the header's jump-to-latest
button while the reader is scrolled back.

That script also keeps the reader's place across Output tabs. Switching tabs replaces the
feed's whole row set, so the open disclosures and scroll offset are saved per tab
(`data-outtab` on each tab button, `data-active-tab` on `#log-feed`) before the click
re-renders, and `climberLog.restoreTab` puts them back from `OnAfterRenderAsync` once
Blazor has patched the new rows. The saved order is oldest first, so rows added at the
chronological end never shift what was already open.

## Turn chrome

A turn's summary names the work and its outcome (`Worked in 5 steps · 2 failed`), and the
`step_finish` line becomes a quiet footer at the end of the body, never a lead-in marker.
Status colour belongs to the row it describes: a failed row is red, and a sibling row in
the same failed turn keeps the ordinary ink. A subagent tab shows that agent's own row
without a turn wrapper, because the tab has no step boundaries of its own; that feed reads
top down and sizes to its rows (`.log.solo` drops the fixed viewport height), so a one-row
tab is one row tall rather than a mostly blank panel. On a narrow panel a trace title wraps
instead of ending in an ellipsis, and a solo row's prompt takes its own full width.

## Adding the next element

1. Read the shadcn page and identify the base composition (`Trace`, `Message`, `Code Block`,
   `Marker`,…). Copy the *shape*, never the classes.
2. Keep the presentation primitives in `Ai/` independent of log parsing. If the element needs
   data transformed, put that in a pure service next to `LogGrouper`/`ToolPresentation` and
   unit-test it.
3. Style it in `ai.css` with the tokens above. Add a semantic alias only if a token is
   genuinely missing; add it to both themes.
4. Preserve the `Plain` fallback. An event the parser does not recognise must still render
   verbatim, or a failed run can render as nothing.
5. Verify with `dotnet test`, the free browser harnesses and a seeded screenshot. A paid run
   is only warranted if the change alters what streams, not how it renders.