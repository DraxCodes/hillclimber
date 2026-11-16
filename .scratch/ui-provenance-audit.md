# Audit: output panel provenance

Status: **complete, read-only (2026-11-16)**. Evidence base for the UI contract and
for the next hillclimb's scope. No code was changed to produce it.

## Question

The output panel is built on ports of shadcn/ui and AI Elements, but the hillclimb
loop keeps producing style-only iterations instead of fixing structure. Is the
panel actually made of the documented elements, and if not, where does it drift?

## Method

Compared three things, per surface:

1. **The port** - every file in `ClimberWeb/Components/Ui/Ai/` against the
   registry source it claims to port
   (`https://ui.shadcn.com/r/styles/base-rhea/<name>.json`).
2. **The composition** - every visible surface in
   `ClimberWeb/Components/Pages/Home.razor` and
   `ClimberWeb/Components/Layout/MainLayout.razor` against the element it renders
   as (`https://ui.shadcn.com/docs/ai-elements`, `/docs/components/base/*`).
3. **The gate** - `scripts/ui/polish.mjs`, `docs/agents/hillclimb-rubric.md`,
   `docs/ai-elements.md`, and the addendum in
   `.scratch/ui-pivot-shadcn/spec.md`.

Severity: **P0** blocks a fair score or breaks semantics/accessibility; **P1**
visible inconsistency or false state; **P2** latent drift (correct today, wrong
for the next caller).

## Verdict

18 visible surfaces: **4 clean ports, 5 partial, 9 bespoke or missing.**

Deduplicated, that is **16 findings**: 5 primitive drifts, 6 missing elements,
5 data/state gaps. Only **4 are visible without interaction** - which is exactly
why a screenshot-scoring judge kept shipping.

---

## 1. Surface inventory

| # | Surface | Rendered as | Documented element | Status |
|---|---|---|---|---|
| 1 | Chat shell | `<section data-slot="chat">` (`Home.razor:179`) | Chat | bespoke |
| 2 | Transcript scroll | `.log` `column-reverse`, `.log-shell::before`, `.log-latest` | Message Scroller | missing |
| 3 | Output strip | `.outtabs` `role="group"` + `aria-pressed` | Toggle Group | bespoke |
| 4 | Message rows | `Message` + `MessageContent` + `Markdown` | Message | clean |
| 5 | User prompt | `Message Align="end"` + `Bubble` | Message | clean |
| 6 | Turn / steps | `TraceCollapsible` + `TraceLine`, hand-composed | Chain of Thought | composed |
| 7 | Plan | `AiPlan` | Plan | partial - data-gated |
| 8 | Subagent / Task | `AiTask` on delegate tabs | Task / Subagent | partial - empty steps |
| 9 | Tool / Terminal | `AiTool` + `CodeBlock` | Tool / Terminal | clean |
| 10 | Reasoning | `AiReasoning` | Reasoning | clean |
| 11 | Sources | `AiSources` | Sources | partial - narrow filter |
| 12 | Errors + footers | bespoke `TraceLine` + `TraceTitle Wrap` | Marker | missing |
| 13 | Working indicator | `.log-working` + dot + elapsed | - (none) | bespoke |
| 14 | Jump to latest | `.log-latest` | `MessageScrollerButton` | missing |
| 15 | Empty + prompts | `Empty` (clean), chips bespoke | Suggestion | partial |
| 16 | Composer | `PromptInput`, raw `<input>` inside | Prompt Input + Input Group | partial |
| 17 | Method + provider pick | `.methodbar`, `.method` | Tabs / Toggle Group | bespoke |
| 18 | Model chip | read-only `Badge` | Model Selector | bespoke, **by design** (spec section 4) |

Composing a turn from `Trace` is legitimate - the docs say every pattern "is a
composition of `Trace` parts". Rows 6 and 18 are accepted deviations, not gaps.

---

## 2. Why the last climb could not see any of this

### A1 - P0: the rhythm check is dead

`scripts/ui/polish.mjs:322` queries `main.stage`. The layout is
`<main data-slot="main">` (`MainLayout.razor:94`), and the check is wrapped in
`if (stage)`, so it has skipped silently since the pivot. Its selector list
(`.panel, .telemetry, .statusline, .spendline, .methodbar, .providerbar`) is
also legacy: only the last two still exist as containers.

The class of defect it would have caught is real and present: `.log` uses
`gap: 0.6rem` = 9.6px (`wwwroot/tw.input.css:337`), off the 4px grid.

### A2 - P0: the rubric has no provenance probe

`docs/agents/hillclimb-rubric.md` has 7 probes: three-second, first-run, state
sweep, width/theme, keyboard, copy, dead-end. None asks whether a surface is the
documented element. The addendum that would have asked -
provenance, state fidelity, composition, model visibility, token - is in
`.scratch/ui-pivot-shadcn/spec.md:250-270` and was never merged.

A judge working from this rubric will score a bespoke row 9/10.

### A3 - P1: the provenance doc describes files that do not exist

`docs/ai-elements.md:9` says the layer lives in `Components/Shared/Ai/`. It does
not. Eight rows map to `AiTurn`, `AiMarker`, `AiCodeBlock`, `AiTraceLine`,
`AiMessage`, `AiMarkdown`, `AiTerminalCard` - none exist. A judge told to "check
provenance" would look up the wrong names.

---

## 3. Drift inside the ports that are otherwise correct

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| B1 | P2 | `Trace` omits the registry's child block `*:has-[>[data-slot=trace-line]]:rounded-lg:focus-visible:ring-3...`, so every linked row re-adds `focus-ring rounded-lg` by hand | `Ui/Ai/Trace.razor:6`; compensation at `Ui/Ai/AiSources.razor:19` |
| B2 | P2 | `TraceIcon` omits `[[data-slot=trace-title]~&]:-ml-1`, the second-icon tightening | `Ui/Ai/TraceIcon.razor:4` |
| B3 | P1 | Collapsible trigger focus ring is `ring-2 ring-ring`; registry is `ring-3 ring-ring/30` | `Ui/Ai/TraceCollapsible.razor:13` |
| B4 | P1 | Expanding has no motion. Registry animates measured height (`transition-[height] duration-200`); the port swaps `hidden` for `overflow-hidden` instantly, while its own comment claims "the same enter animation" | `Ui/Ai/TraceCollapsible.razor:25` |
| B5 | P2 | `OnToggle` is not required, so a caller can render a trigger whose `aria-expanded` never changes | `Ui/Ai/TraceCollapsible.razor:37-41` |

`TraceLine`, `TraceTitle` and `TraceDescription` match the registry exactly, plus
two deliberate `TraceTitle` additions (`Title`, `Wrap`). They are not the problem.

---

## 4. Missing elements

### C1 - P0: no `Marker`

Registry Marker is `w-full min-h-4` with `separator` and `border` variants.

Today: errors are `TraceLine Status="error"` (`Home.razor:348`), footers are
`TraceLine` + `Wrap`, and the turn cost line is a bare `<p>`
(`Home.razor:329`).

Cost: the transcript has **no chapter breaks**. Long runs read as one
undifferentiated column. Highest-value missing element for composition.

### C2 - P0: no `Message Scroller`

Registry provides the viewport (`scroll-fade-b`, `scrollbar-thin`,
`scrollbar-gutter-stable`, `overscroll-contain`, `contain-content`,
autoscroll-aware scrollbar hiding) and `MessageScrollerButton` with
`data-active` transitions.

Today: `.log` is `flex-direction: column-reverse` with `overflow-anchor: none`
(`wwwroot/tw.input.css:337`), a hand-drawn top fade, custom scrollbar CSS, and a
hand-rolled `.log-latest`. Items render newest-first and the user prompt is
appended last so it appears at the top (`Home.razor:397`).

Cost: DOM order is the reverse of reading order. A screen reader starts at the
newest turn; `Ctrl+Home` goes to the latest, not the beginning. This is a
sequence problem (WCAG 1.3.2), not a labelling one.

### C3 - P1: no `Suggestion`

Chips are hand-built at `Home.razor:382` under a bespoke
`data-slot="empty-suggestions"`.

### C4 - P1: no `Tabs` — **closed 2026-11-16**

`.methodbar` (`Home.razor:80`) is a labelled group of toggle buttons with
`aria-pressed`, matching the output strip (`Home.razor:210`). Neither switcher
claims the tab keyboard contract it does not implement.

### C5 - P1: no `Dialog` / `Sheet`

The folder picker is `role="dialog" aria-modal="true"` at `Home.razor:508`; the
mobile session rail is a bespoke drawer in `MainLayout.razor`. Neither has a
focus trap or a guaranteed Escape.

### C6 - P2: no `Input` / `Input Group`

Folder path, max iterations and PIN are raw `<input class="focus-ring h-8 ...">`
inside `PromptInput`'s popover.

### C7 - accepted: no `Model Selector`

Deliberate per spec section 4 (read-only chip). Record it as an exception; do not
keep calling it the element.

---

## 5. Data and state fidelity - the part no styling pass fixes

### D1 - P0: Plan is gated at two or more subagents in one turn

`HandoffSteps` returns empty below two (`Home.razor:1030`), and `AiPlan` only
renders at `handoff.Count >= 2` (`Home.razor:310`). A hillclimb handoff spread
across turns therefore renders **zero** Plan, and a single subagent on the
primary tab renders as `AiTool`. The spec's composition probe would fail on the
product's own happy path.

### D2 - P1: `AiTask` always receives empty steps

`Steps="Array.Empty<PlanStep>()"` (`Home.razor:969`). The Task element's defining
nested steps are never populated.

### D3 - P1: Sources claims MCP, filters web only

`TurnSources` documents "Web or MCP targets" but accepts only `webfetch` and
`websearch` (`Home.razor:1044-1052`). An MCP-driven run shows no sources while
the comment says otherwise.

### D4 - P1: `CodeBlock` has no max height

One long `dotnet test` output makes its turn unreadable.

### D5 - P2: `TraceTitle` truncates by default

Only markers pass `Wrap`, so a long tool title at 375px is hover-only.

---

## 6. Chrome (outside the panel, visible)

- Session rail and mobile drawer are bespoke (no Sheet/Dialog).
- `.log` forces `var(--font-mono)` over prose that Message renders sans.

---

## 7. Recorded decisions

### Decision 1 - keep `column-reverse`, record the cost

Rejected alternative: chronological DOM + autoscroll + scroller button.

It is the correct destination, but it trades a free invariant (the browser pins
the visual bottom, so a streaming row never moves the viewport) for an autoscroll
policy that must be designed. It also invalidates 12 tests in
`StepScrollMathTests.cs` that pin browser-observed values, and the two scroll
conventions in `App.razor` (reverse for the combined feed, forward for solo tabs).

**EXCEPTION 1.** The combined transcript renders newest-first instead of the
chronological DOM that Message Scroller documents. Cost: reading order and
`Ctrl+Home` are reversed for assistive technology. Temporary; see Phase 3.

Doing it inside a visual climb would also mix a structural refactor with
appearance, and the rubric cannot separate which change moved the score.

### Decision 2 - gate first, then one scoped climb

Do not buy a bigger model to compensate for a gate that cannot see the defects.
Fix what the gate can see, then climb.

### Decision 3 - deviations must be written down

A deviation from a documented element is allowed only if it is named in the
contract with its cost. Unnamed drift is a flaw; named drift is a decision.

---

## 8. Plan

### Phase 1 - contract and gate (free, no browser)

1. Write `docs/ui-contract.md`: the surface table from section 1, the provenance
   rule from Decision 3, and the exception register.
2. Merge the spec addendum into `docs/agents/hillclimb-rubric.md` as probes 8-12:
   provenance, state fidelity, composition, model visibility, token. Below 8 must
   name a provenance or composition violation, not a spacing nit.
3. Fix `polish.mjs`: target `[data-slot=main]`, replace the legacy selector list,
   and fail loudly when the stage is missing instead of skipping.
4. Rewrite `docs/ai-elements.md` against section 1.

### Phase 2a - one climb, 3 iterations, five visible gaps

`Marker` for errors, footers and dividers; collapse motion and focus ring (B3,
B4); `CodeBlock` cap (D4); Plan across turns (D1); sources filter (D3). Four
component changes and one data fix.

Target **8**, which is the mechanical maximum. Probe 13 is expected to report the
same flatness before and after, so this phase cannot exceed 8 by design; the
visual pass is 2b.

Everything else stays on the debt register so the judge cannot score 9 by
pretending it is absent.

### Phase 2b - art direction pass, its own climb

The conservative decisions in [`docs/ui-art-direction.md`](../docs/ui-art-direction.md):
type split, 8px/24px rhythm, prose measure, turn hierarchy, header demotion, accent
budget, radius. Scored on probe 13.

Expressive moves from the same file come after that pass holds at 9, one at a
time, each with a screenshot and a rationale.

### Phase 3 - chronological transcript (its own task)

Message Scroller adoption: chronological DOM, autoscroll policy, scroller button.
Browser-verified, with `StepScrollMath` and its tests re-derived.

### Not now

Message Scroller, Tabs, Dialog, Suggestion, Input Group, Model Selector as a
porting batch. Roughly nine components, mostly invisible at rest, and they change
scroll and keyboard behaviour across the whole panel.

---

## 9. Debt register

| ID | Debt | Phase |
|---|---|---|
| A1 | rhythm check dead | 1 |
| A2 | no provenance probe | 1 |
| A3 | stale element doc | 1 |
| B1, B2, B5 | latent `Trace` drift | 2 if touched |
| B3, B4 | focus ring, collapse motion | **closed in 2a** |
| C1 | Marker absent | **closed in 2a** |
| C2 | Message Scroller absent, reverse DOM | 3 |
| C3 | Suggestion absent | 3 |
| C4 | Tabs absent, methodbar ad-hoc tablist | **closed in 2c** |
| C5 | Dialog/Sheet absent | 3 |
| C6 | Input/Input Group absent | 3 |
| C7 | Model Selector - accepted exception | never |
| D1 | Plan gated per turn | **closed in 2a** |
| D2 | Task steps always empty | 2 |
| D3 | sources filter narrow | **closed in 2a** |
| D4 | CodeBlock uncapped | **closed in 2a** |
| D5 | titles truncate by default | 3 |
| R1 | feed row gap 9.6px, off the 4px grid | 2 |

## 10. How to re-run

Ports: `https://ui.shadcn.com/r/styles/base-rhea/<name>.json`
Docs: `https://ui.shadcn.com/docs/ai-elements`, `https://ui.shadcn.com/docs/components/base/<name>`

Re-read the six port files that can drift (`Trace`, `TraceLine`, `TraceIcon`,
`TraceTitle`, `TraceDescription`, `TraceCollapsible`) against the registry
whenever an element is touched. The remaining drift is in composition and data,
not in classes.

## 11. Addendum: what the repaired harness sees (2026-11-16)

Phase 1 replaced the rhythm check in `scripts/ui/polish.mjs`. The old one read
`marginBottom` off `main.stage`, which no longer exists, and skipped silently.
The new one measures box gaps over two stacks: the shell regions
(`context-strip`, `chat`, `composer-dock`) and the feed rows
(`#log-feed > .log-item`).

Verified against a live server:

- `node scripts/ui/polish.mjs` - **100 UPS, 0 defects in 560 checks, exit 0.**
- With a transcript present, feed row gaps measure **9.59375px** (`gap: 0.6rem`
  in `wwwroot/tw.input.css`), which is off the 4px grid. The default harness state
  has no transcript, so the check only fires once a seeded run or history exists.

That 9.6px gap is a real R4 violation, not a false positive. It is debt R1 in
section 9.

The 1px seam between shell regions is the header's own border; seams under 2px are
treated as hairlines rather than spacing.