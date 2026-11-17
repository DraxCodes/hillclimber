# Hillclimb rubric addendum — HillclimberUI

This is the project addendum named by `docs.rubricAddendum` in
`.opencode/workflow.json`. It extends whichever hillclimb rubric is selected
(`references/rubrics/<name>.md` in the `hillclimb` skill); it never lowers the
floor or weakens a level. When the two disagree, the stricter reading wins.

This project's default is `perception`, because the app's primary surface is
visual. The UI probes below apply only when `perception` is the selected rubric. A
run that selects `performance` (for example, build or test time) or `completeness`
(for example, a spec review) uses that rubric's own probes; this addendum then
contributes only the floor, the caps that name a UI contract, and the recorded
exceptions.

With `perception`, the rubric's probes 1–7 apply here unchanged. This file adds the
probes that only make sense for this app's AI surfaces, the caps they imply, and
the exact floor.

## Additional probes

8. **Provenance probe.** For every visible AI surface, name the AI Element it is and
   open the doc that defines it. A bespoke AI block where a documented element
   exists is a flaw. For every non-AI surface, name the shadcn base component; a
   one-off equivalent is a flaw. The surface map in
   [`docs/ui-contract.md`](../ui-contract.md) is the expected answer — a surface
   that disagrees with it is a flaw even when it looks right.
9. **State fidelity probe.** For `Prompt Input`, `Reasoning`, `Plan`, `Sources`,
   `Task`, `Tool` and `Inline Citation`, exercise closed, open, streaming,
   complete, failed and disabled in both themes. A component with only its resting
   state is a flaw, and so is a trigger whose `aria-expanded` never changes.
10. **Composition probe.** A multi-subagent handoff renders as one Plan whose steps
    settle in order, not as unrelated cards; `Reasoning` collapses when its stream
    ends; `Sources` expands to real URLs; a failure opens to its output; a note or
    a divider is a `Marker`. Styling a composition you did not compose is a flaw.
11. **Model visibility probe.** The composer names the exact provider and model
    before send, while streaming and when disabled; the name does not truncate at
    375px.
12. **Token probe.** Sample the rendered palette against `app.css` in both themes. A
    colour outside the documented mapping, a hard-coded hex inside a port, or a
    token that exists in one theme only is a flaw unless a recorded exception names
    it.
13. **Art direction probe.** Score the screen against
    [`docs/ui-art-direction.md`](../ui-art-direction.md). At three seconds, name the
    primary level and check that only it competes for attention. Then check the
    measure (prose 60–75ch), the rhythm (8px inside a group, 24px+ between
    groups), the type scale (three sizes plus one micro), the accent budget (one
    job), and that mono appears only on data. A screen that passes probes 1–12 but
    has no hierarchy is an 8, not a 9.

Probes 8–12 are the addendum from `.scratch/ui-pivot-shadcn/spec.md`. Probe 13
scores [`docs/ui-art-direction.md`](../ui-art-direction.md). The contract they
check is [`docs/ui-contract.md`](../ui-contract.md).

## Caps

- **Provenance cap:** a UI surface with a provenance, composition or state
  violation that is not recorded in the contract cannot score above 7. A recorded
  exception does not cap the score — it is judged on its stated cost. See
  [`docs/ui-contract.md`](../ui-contract.md).
- **Art direction:** passing probes 1–12 caps the score at 8. A 9 or a 10 requires
  probe 13, judged from screenshots against
  [`docs/ui-art-direction.md`](../ui-art-direction.md). Compliance is the floor for
  a good score, not the score.

## Supporting evidence

`scripts/ui/polish.mjs` is supporting evidence, not a threshold: it reports a UI
Polish Score and exits 0 whatever the score, so only a harness error or a detected
run fails it. Do not turn its UPS into a gate until backlog issue 05 adds one.

## The floor

The seam's `checks` are authoritative. In this repo they are:

- **build:** `dotnet build --nologo`
- **test:** `dotnet test --nologo`
- **free:** `node theme-nav.mjs`, `node security.mjs`, `node providers.mjs`, from
  `scripts/ui`
- **paid:** `node surfaces.mjs` from `scripts/ui` — runs real `opencode` runs and
  spends tokens; execute only with explicit approval, and say **not run** otherwise.

Stop the dev host before `dotnet test` on Windows. The Playwright harness lives in
the temp `climb-ui` folder; a visual change is verified with a seed and screenshots,
then the seed is removed. Transcript states the committed playback fixture can
reach start with `CLIMBER_SEED` (see `README.md`).

A red floor cannot score 8 and cannot ship. Label anything not run as **not run**.