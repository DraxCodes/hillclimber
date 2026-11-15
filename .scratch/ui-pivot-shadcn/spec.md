# Spec: T3-Code shell on shadcn/ui + AI Elements

Status: **settled 2026-11-15** — architecture A (Razor port + Tailwind 4), home + activity +
status, composer chips, read-only model chip. Hillclimb target 8, cap 5 (6 only if below 7).

## The report (user's words, condensed to the decisions)

> I want to pivot to a t3-code like design for the whole UI, think something like Claude Code
> where we have the header/nav with our run/hillclimb selector, the connection info for our
> providers, the run estimation, the platform swapping buttons and the
> status/spent/tokens/elapsed all stay and stay at the top then underneath is a larger window
> which is for our AI output with a prompt entry box at the bottom. Perhaps to the left we
> have a session history of some sort, you don't have to implement the session history but you
> should get the basic UI of it set up. On the prompt entry box we should have the ability to
> see which model is being used for a given session, specifically which model is being used
> for the prompt the user is sending.
>
> We should most definitely use Shadcn's AI-Elements for this now, we gave it a try making our
> own but I'm not happy with the result still. All AI related UI elements in our app should
> make use of the AI-Elements from ShadCN while all other components the app may use will make
> use of the general shadcn components, you may be tempted to use a general textbox component
> for the prompt but DO NOT do this, use the prompt input component instead. Another example
> is the AI Output we render ENSURE you use the Chat component here. Ensure all colour styling
> across the app remains near the same as what we have currently.
>
> I want to try hit an 8 for the hillclimb but DO NOT go further than 5 iterations unless
> you're unable to land on a 7.. if you can't land a 7 from the hillclimb then you can do 6
> iterations and take whatever result we end up with, if the final iteration hillclimb has
> actual errors or severe issues though, you can fix those before finishing.
>
> Ensure you take plenty of screenshots or run the app to directly observe the changes made
> and how they look. You may have to stream data to the UI to see components like Message and
> Task and Chat in action, this is allowed. Inline citations should be used for links ...
> plan should be used for multistep tasks such as when you hand off to multiple sub agents ...
> Reasoning components should be used where you're streaming reasoning output, it collapses
> when the reasoning output stream is finished ... When you reach out to the web or other MCPs
> for sources, it should use the sources component. ... if you need to adjust the rubric to
> reflect this before starting, please do.

## What exists today (verified)

- .NET 11 Blazor Server, `@rendermode InteractiveServer`, single user, PIN `1234`, port 5179.
- `ClimberWeb/Components/Pages/Home.razor` is a vertical stack of six blocks:
  `statusline` (pulse + status word + message), `methodbar` (run | hillclimb), `spendline`
  (per-provider connection dots + estimated spend), `providerbar` (platform buttons),
  `telemetry` (status / spent / tokens / elapsed, or iteration / best score / last run),
  `configure` (target folder + browse, prompt textarea, max iterations, agent chips,
  approvals, start / stop / lock), and `panel-flush` output (working indicator, subagent tabs,
  reverse scroller). A file-browser modal sits at the end of the page.
- The output currently renders through bespoke components in
  `ClimberWeb/Components/Shared/Ai/`: `AiTurn`, `AiMessage`, `AiReasoning`, `AiTerminalCard`,
  `AiTraceLine`, `AiMarker`, `AiMarkdown`, `AiCodeBlock`, `AiIcon`, styled by
  `wwwroot/ai.css` (13 KB). This is the layer the user is unhappy with.
- Data shaping already exists and is tested: `LogFormatter` → `LogEntry`, `LogGrouper` →
  `OutputItem` (`Message`, `Turn`, `TraceGroup`, `Marker`, `Error`, `Unrecognized`, `Plain`),
  `TurnPresentation`, `ToolPresentation`, `MarkdownParser`, `OutputActivity`.
- Theme tokens live in `wwwroot/app.css` (`:root` + `[data-theme="dark"]`): warm off-white /
  warm near-black, `--accent` #007aff / #6b8fff, `--line`, `--surface-2`, 4–6 px radii,
  mono-first chrome. The user wants colours to stay near these.
- Providers carry their model id: `ProviderDescriptor.Model`, with
  `surplus/deepseek-v4.1-flash-1017` and `openrouter/deepseek/deepseek-v4.1-flash` as
  shipped defaults. `IProviderRegistry` already normalises the prefix. The composer can show
  the exact model with no new config.
- The shell has no left rail, no session list, no composer dock and no pre-run cost estimate.
  "Run estimation" in the report is taken to mean the existing estimated-spend readout; a
  pre-run estimate would be new scope.

## What the docs say (research, 2026-11-15)

- AI Elements moved into shadcn/ui: **`https://ui.shadcn.com/docs/ai-elements`**.
  `elements.ai-sdk.dev` is deprecated at the end of November 2026. Do not build on it.
- **There is no package.** Every element is a composition of shadcn/ui components copied into
  the project: "There is no package to install. The code is yours." The official stack is
  React 19 + Tailwind 4 + Radix/Base UI + `@ai-sdk/react` + `streamdown`/Shiki.
- The current element set is: Chain of Thought, Chat, Checkpoint, Confirmation, Context,
  File Tree, Inline Citation, Message, Model Selector, Plan, Prompt Input, Queue, Reasoning,
  Sources, Subagent, Task, Terminal, Tool, plus `Message Scroller` and `Trace` as base
  primitives.
- Latest published versions: `shadcn` CLI **4.23.0**, `@shadcn/react` **0.3.1**, legacy
  `ai-elements` **1.9.1** (legacy; not the path).
- Local doc set (uncommitted, outside the repo):
  - `%TEMP%\climb-ui-docs\shadcn-md\ai-elements\*.md` — full TSX source for all 13 needed
    elements (Chat, Message, Prompt Input, Model Selector, Reasoning, Plan, Task, Sources,
    Inline Citation, Tool, Terminal, Subagent, Chain of Thought).
  - `%TEMP%\climb-ui-docs\shadcn-md\components\*.md` — Button, Badge, Card, Collapsible,
    Command, Dropdown Menu, Empty, Hover Card, Input Group, Message Scroller, Popover,
    Scroll Area, Separator, Sidebar, Skeleton, Spinner, Tabs, Textarea, Tooltip, Trace,
    Code Block, Attachment.
  - `%TEMP%\climb-ui-docs\shadcn-md\guides\*.md` — AI Elements intro, theming, CLI,
    components.json, Tailwind v4, dark mode, Typeset.
  - `%TEMP%\climb-ui-docs\blazor-ports\` — third-party port notes.
- Blazor options that exist but are **not** the official components:
  - **ShadcnBlazor** (NuGet `ShadcnBlazor` 1.0.22) — shadcn-styled Razor components.
  - **NeoUI.Blazor** 3.x — Tailwind-based shadcn-style component set.
  - **ShellUI** — standalone-Tailwind shadcn port, 93 components, includes an AI chat kit.
  None ships the official AI Elements compositions, so they would be an approximation with a
  second approximation layered on top.

## The architecture question

Shadcn/ui is a set of React components; AI Elements is a set of React compositions. A Blazor
app can consume them in four ways. This is the decision to settle.

### A — Razor port on the shadcn token/utility layer (recommended)

Keep Blazor Server. Add Tailwind 4 (standalone CLI, no Node at runtime) and define the shadcn
CSS variables in `app.css` from the existing palette. Translate the AI Elements TSX into Razor
components under `Components/Shared/Ai/`, keeping the official markup, ARIA roles and class
strings; re-implement only the small JS behaviours the app needs (collapsible, dropdown,
tooltip) in vanilla JS or Blazor state.

- **Pros:** one runtime, one deploy, streaming stays in Blazor; exact shadcn spacing, radii,
  type scale and muted-foreground because the classes are copied, not retranslated; general
  shadcn components can be ported incrementally; theme maps 1:1 onto today's palette.
- **Cons:** not the literal React code; Radix behaviour must be recreated and can differ
  (focus traps, menu positioning); a Tailwind build step appears in the repo (generated CSS
  committed, so `dotnet run` stays plain); "latest shadcn" means the current registry snapshot,
  not a package version.
- **Risk to watch:** a port that only copies the look and not the interaction states is what
  the user already rejected. The gate must check hover/focus/disabled/open states, not just
  pixels.

### B — React island for the chat surface only

Keep Blazor for chrome, auth, run control and streaming; add a Vite + React 19 + Tailwind 4
bundle that mounts the real `Chat`, `Message`, `Prompt Input`, `Reasoning`, `Plan`, `Task`,
`Sources` and `Inline Citation` in the main column. Blazor pushes events over JS interop.

- **Pros:** literal components and behaviour; future AI Elements upgrades are copy-paste;
  closest to shadcn's intended use.
- **Cons:** two runtimes and a Node build in the app path; a new serialisation contract and
  event bridge; theme sync across the boundary; PIN/auth and configure state split between
  C# and JS; the header would still be Razor unless the whole page moves, so the seam is
  visible; largest scope of the Blazor-preserving options — likely more than 5 iterations.

### C — Whole front end in React, Blazor as API

- **Pros:** maximum fidelity; the whole app becomes a normal shadcn project.
- **Cons:** rewrite of auth, configure, provider approval, file browser, activity and status
  pages; run orchestration exposed over an API and SSE; high regression risk. Far beyond the
  iteration cap. Not recommended for this task.

### D — Third-party Blazor port (ShadcnBlazor / NeoUI / ShellUI)

- **Pros:** fastest to a shadcn-looking shell; real components; no new build chain for the
  general set.
- **Cons:** no official AI Elements, so the AI surfaces are still a hand port and will lag the
  docs; a second design system's tokens and conventions fight the current ones; the user's
  complaint ("we made our own, not happy") is likely to persist in a different form.

**Recommendation: A.** It is the only option that keeps the streaming architecture, the PIN
boundary and the existing run control intact while giving the port the official class strings
and structure. B is the fallback if the port's interaction fidelity cannot reach 8 within the
iteration cap; C and D are not recommended for this task.

## Proposed design (what A would build)

### Layout

```
┌──────────────────────────────────────────────────────────────────────┐
│ header: brand · nav · lock/theme                                     │
│ run|hillclimb · provider dots · spend/estimate · platform · telemetry│
├───────────────┬──────────────────────────────────────────────────────┤
│ sidebar       │  chat column (Message Scroller)                      │
│ sessions      │    user Message (the prompt that started the run)    │
│  · current    │    assistant turn:                                   │
│  · empty      │      Reasoning (collapses when the stream ends)      │
│               │      Plan (animated while a handoff is in flight)    │
│               │      Task rows for each subagent                     │
│               │      Trace rows: Tool / Terminal                     │
│               │      Sources (webfetch / MCP)                        │
│               │      prose Message + Inline Citation links           │
│               ├──────────────────────────────────────────────────────┤
│               │  Prompt Input dock                                   │
│               │   [folder chip] [max iter]        [model chip] [send]│
└───────────────┴──────────────────────────────────────────────────────┘
```

- At 375 px the sidebar becomes a `Sheet`/drawer behind a menu button; the chat and composer
  keep the full width. The header collapses to two rows and the telemetry wraps as badges.
- The configure panel is dissolved: target folder and max iterations become chips in the
  composer's top row (popover editors); send starts the run, stop replaces send while
  running; approvals and errors surface as inline `Alert`s above the composer. The file
  browser stays a `Dialog`.
- Auth stays a centered `Card` + `Input` + `Button` in the same shell.
- Session history is a real, honest shell: the current run as one row (method, status,
  elapsed) plus an `Empty` state for earlier sessions. No fake sample rows, no "coming soon"
  copy.

### AI surfaces → AI Elements (Razor ports)

| Surface | Element | Data source |
| --- | --- | --- |
| Transcript container + auto-follow | Chat + Message Scroller | `OutputItem` list, existing scroll script |
| Agent prose, prompt echo | Message | `Message` items, prompt text |
| Composer | Prompt Input (+ Input Group, Attachment) | `PromptBox`, start/stop |
| Model readout | Model Selector (read-only chip first) | `ActiveProvider.Model` |
| Streaming reasoning | Reasoning (auto-collapse when done) | `LogKind.Reasoning` |
| Hillclimb / subagent handoff | Plan (animated while active) | climb iterations, subagent groups |
| Subagent work | Task / Subagent | `LogKind.Subagent` |
| Tool calls | Tool (+ Terminal for shell output) | `ToolPresentation` |
| Web / MCP fetches | Sources | `webfetch` and MCP tool calls |
| Links in prose | Inline Citation | `MarkdownParser` links |

### General chrome → shadcn base components

Sidebar, Button, Badge, Card, Tabs, Separator, Scroll Area, Tooltip, Popover, Dropdown Menu,
Command, Skeleton, Spinner, Empty, Collapsible, Input, Input Group, Textarea (non-AI only),
Dialog/Sheet, Alert.

### Palette

Map shadcn tokens to the current values rather than adopting shadcn's default neutral ramp:

| shadcn token | current token |
| --- | --- |
| `--background` | `--bg` |
| `--foreground` | `--ink` |
| `--card` / `--popover` | `--surface` |
| `--muted` | `--surface-2` |
| `--muted-foreground` | `--ink-3` |
| `--border` | `--line` |
| `--primary` | `--accent-strong` |
| `--primary-foreground` | `--accent-ink` |
| `--destructive` | `--red` |
| `--ring` | `--focus` |

Dark theme uses the existing `[data-theme="dark"]` values. `--radius` becomes 0.375rem to match
shadcn's control radii while staying close to today's 4 px.

## Decisions (settled 2026-11-15, with the user)

1. **Architecture: A — Razor port + Tailwind 4.** Keep Blazor Server; add Tailwind 4 with the
   shadcn token layer mapped onto the current palette; translate the official AI Elements TSX
   into Razor, preserving markup, classes, ARIA and states. Generated CSS is committed so
   `dotnet run` needs no Node.
2. **Page scope: home + `/activity` + `/status`.** All three wear the new shell. The home chat
   is the deep surface; the other two are general-shadcn restyles that share the header,
   sidebar and tokens.
3. **Configure placement: composer chips + inline alerts.** Target folder and max iterations
   live in the Prompt Input's top row with popover editors; send starts, stop replaces send;
   approvals, preflight warnings and errors are inline alerts above the composer. The
   configure panel disappears.
4. **Model control: read-only chip.** The composer names the exact provider + model. No
   per-run switching in this task.
5. **Session history:** an honest current-run row plus an `Empty` state for earlier sessions.
   No fake rows and no "coming soon" copy.

## Iteration cap and rubric addendum

Target **8/10**. Hard cap **5 iterations**. If the score is below 7 after iteration 5, run a
6th and take the result. Fix only actual errors or severe issues after the final iteration.

Add these probes to `docs/agents/hillclimb-rubric.md` before scoring. They are evidence
requirements, not new score levels:

- **Provenance probe.** For every visible AI surface, name the AI Element it is and open the
  local doc that defines it. A bespoke AI block that has a documented element is a flaw. For
  every non-AI surface, name the general shadcn component. One-off equivalents are a flaw.
- **State fidelity probe.** For `Prompt Input`, `Reasoning`, `Plan`, `Sources`, `Task`,
  `Tool` and `Inline Citation`, exercise closed/open/streaming/complete/disabled states in
  both themes. A component that only has its resting state is a flaw.
- **Model visibility probe.** The composer names the exact provider + model before send,
  while streaming and when disabled; the name does not truncate at 375 px.
- **Composition probe.** A multi-subagent handoff renders as one Plan whose steps animate in
  order, not as unrelated cards; Reasoning collapses when its stream ends; Sources expands to
  real URLs; citations open the linked source.
- **Token probe.** Sample the rendered palette against `app.css` in both themes; a new colour
  outside the documented mapping is a flaw unless the spec names it.

## Evidence plan

- Temporary seed (one throwaway file + one hook, `CLIMBER_DEMO`, deleted before commit) that
  drives real opencode event shapes through the real formatter/grouper: a user prompt, a
  reasoning stream that ends, a three-step hillclimb plan, two subagents, shell/read/edit
  tools, a failed tool, a webfetch with two URLs, prose with two links, and a final answer.
- Playwright probes at 900 and 375, light and dark: Chat/Message present, exactly one
  Prompt Input (no `<textarea class="input">`), model chip text matches the provider model,
  Reasoning auto-collapses on completion, Plan steps animate/settle, Sources and citations
  open, no horizontal overflow, zero console errors, focus visible on every stop.
- Screenshots of: unlocked idle, streaming with reasoning + plan, completed transcript,
  auth, empty history, error, 375 collapsed sidebar, dark variants.
- Free floor: `dotnet build`, `dotnet test`, `theme-nav`, `providers`, `security`, `polish`.
  Paid `surfaces.mjs` **not run** unless asked. If selectors change, update the harness and
  say plainly it was not executed.

## Out of scope (unless the user widens it)

- Real session persistence, resume, branching or a session store.
- Auth changes, PIN flow changes, provider configuration UI changes.
- Pre-run cost estimation (no such feature exists today).
- Markdown renderer replacement beyond what citations need.
- Reworking the run/hillclimb engines or log services.
- Committing the downloaded docs.

## Risks

- **Port drift.** Copying classes without the interaction states is the failure mode that
  produced the current layer. The gate scores states, not pixels.
- **Tailwind in a Razor app.** Arbitrary variants and `group-data-[state=open]:` work in
  class attributes, but Razor treats `@` specially; classes containing `@` must be avoided or
  escaped. Generated CSS is committed so the app still builds without Node.
- **Blazor re-renders vs. native disclosure.** Existing lesson from `scroll-to-expanded-step`:
  keep `open` state in C# where it must survive, use `@key`.
- **Streaming scale.** A long run can hold thousands of items; keep the existing reverse
  scroller or the Message Scroller's anchor logic, not a naive `scrollTop` on every event.
- **Auth boundary.** The composer and chat must not render before the PIN unlock, and the
  seed must not bypass auth.