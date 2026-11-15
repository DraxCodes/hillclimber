# Spec: Output panel readability — activity, density, markdown

Status: open. Reported by the user on 2026-11-15, after the
[output-log-formatting](../output-log-formatting/spec.md) hillclimb shipped at 8/10.
Nothing here is designed or implemented yet; the approach has to be settled with the user
first (AGENTS.md preference 1).

## The report (user's words)

> The next problem will be addressing some issue I still have with the Output panel and log
> formatting. There's no visual indication that the model is currently working, you have a
> glowing/pulsing effect on some elements that would do a good job at indicating it's
> working on a step or job or something currently but it's barely ever seen and not applied
> to sure it actively working. I believe it was intended to show activity too so not sure
> why it seems to have been forgotten about. When you pass off to a sub agent such as the
> Quality Gatekeeper or the Security Auditor, there's also no visual indication that it's
> working on the other tab either. Far too much is now hidden, absolutely everything is
> collapsed by default, this should not be the case, if you see examples from most modern
> LLM Clients like CoPilot, Claude Code, T3-Code, and many more, you will find they handle
> this much better. All Markdown is completely unformatted whenever it's used. in the
> logger, this should be easy to actually render so I'm not sure why that wasn't chosen
> either. There are more problems but we will attack those after the things I mentioned and
> itterate on it.

## What the code does today (verified, with pointers)

1. **The shimmer exists but almost never shows.** `ai.css` animates `.ai-turn-summary` and
   `.ai-trace-title` under `.ai-status-running` (`ai.css:125`, `:266`, `:433` for terminals).
   A turn only gets `StepStatus.Running` while it is *both* open and live —
   `TurnPresentation.For` returns `Running` for `streaming = open && live` only, and
   `open` comes from the grouper's final unterminated turn. opencode emits `step_finish`
   at the end of **every step**, so the last turn usually has a footer, is `open: false`,
   and is not live. The result: the animation is correct but its window is the seconds
   between one step finishing and the next starting.
2. **Nothing carries activity at the panel or tab level.** The output header
   (`Home.razor:280-288`) is `[03] output` + jump-to-latest. The `.outtab` buttons carry
   `active` and `aria-selected` only. The only live signal is the status card far above the
   fold, so a reader watching the transcript sees a static panel.
3. **A subagent's own tab shows nothing about the subagent.** On the primary tab a
   subagent row reads `plan-architect — see its tab`; on its own tab the same row reads
   `plan-architect subagent` plus the prompt. Neither the tab button nor the feed says it
   is still working, and the row only shimmers if its `LogEntry.Status` happens to be
   `Running` — the tab strip has no equivalent.
4. **Everything is collapsed once settled.** `TurnPresentation.For` opens a turn only when
   streaming or when something failed; `AiReasoning` is a closed `<details>`;
   `AiTerminalCard` opens only while running, or when failed with ≤ 6 lines of output;
   trace rows are closed. A finished run therefore reads as a stack of one-line summaries,
   with the answer prose the only expanded content.
5. **Markdown is literal.** `AiMessage.razor` is `<div class="ai-message">@Text</div>` —
   Razor-encoded text with `white-space: pre-wrap`. Real answers arrive with `**bold**`,
   backticks, `- ` bullets and fenced blocks, all shown verbatim (seen live in
   `%TEMP%\climb-ui-shots\demo-done-panel.png`).

## Why this is worth a hillclimb

It is perceived-quality work on a surface a reader stares at: the rubric's 8 line ("nothing
trips up a first-time user, nothing looks unfinished") is the honest bar, and the previous
effort already set the visual language. It is also measurable in places (time-to-first-
working-signal, rows visible at 375, markdown cases rendered).

## Open questions to settle before coding

1. **Activity model.** Is the panel-level signal a single "the run is working" affordance
   (header dot/pulse + elapsed), per-row shimmer for whatever is genuinely in flight, or
   both? What is the source of truth — `ActiveRunning` plus the last row's status, or a new
   "current activity" derivation in a pure service (tested, like `TurnPresentation`)?
   Note the trap in finding 1: per-step `step_finish` events make "last turn is open" a
   poor liveness test.
2. **Subagent activity.** Does a subagent tab get a pulse on its button, a count of running
   calls, or just the row's shimmer? What does the primary-tab pointer row show while the
   delegate works, and what does it show when the delegate failed?
3. **Expansion policy.** What is open by default in a settled transcript — the last turn
   only, the last N turns, every turn until the reader collapses one, or never auto-collapse
   anything that is new since the tab was last seen? How does that interact with the
   per-tab scroll/expansion state the previous effort added (`data-outtab` /
   `climberLog.restoreTab`), which deliberately remembers the reader's own choices?
4. **Markdown.** Which subset, and inline-only or blocks too? Options: a small hand-written
   inline renderer (bold/italic/code/links) over Razor-encoded text, a blocks renderer
   (headings, lists, fences, tables) for the message body, or a dependency. It must stay
   XSS-safe — the repo's security posture says "Razor encodes output, there are no
   `MarkupString`/`Html.Raw` sinks" — and terminal output must keep its verbatim `AiCodeBlock`.

## Out of scope (until the user says otherwise)

- The remaining spec mismatches recorded in the previous effort's review (error-text
  truncation, lowercasing unknown-event titles, the `climberCopy` name, non-zero exit
  forcing `Failed`, failed cards staying closed above six lines, exit-code key guessing).
- The ten code smells listed in
  [output-log-formatting/spec.md](../output-log-formatting/spec.md) under "Review
  follow-ups".
- Anything that changes what streams (that would need the paid `surfaces.mjs` run).

## Evidence plan

Seeded sample data (the previous seed was deleted before shipping; rebuild one from the event
shapes in the older spec), screenshots at 375 and 900 in both themes, plus a live run to
judge the activity signal — a static screenshot cannot show a pulse. Keyboard pass over the
new default expansion. `dotnet test` plus the free harnesses each iteration; `surfaces.mjs`
only with approval.