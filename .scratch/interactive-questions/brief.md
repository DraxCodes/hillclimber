# Council brief — interactive model questions

## Question

Should ClimberWeb render opencode's interactive questions as answerable UI in the transcript —
and if so, in what shape, over what transport, and at what cost to the run pipeline?

## Fixed point

`35f1d42` (council setup). Working tree clean.

## Verified facts

- `opencode run` is one-shot and non-interactive. It has **no** question flag; `--auto` only
  auto-approves permissions. When a run needs input it cancels with: *"This non-interactive
  run cannot ask the user questions, so the question was cancelled."*
- `OpenCodeRun.Request` builds an argv list: `opencode run <prompt> --session <id> --agent <a>
  --model <m> --format json`. No stdin channel is used or opened.
- `OpenCodeRun.CommandLine` renders that invocation verbatim into the output panel.
- The app is local and single-user: loopback, PIN-gated, sessions persisted as JSON under the
  user's app data, one live run per process.
- opencode offers a server mode (`opencode serve`) with a question/permission reply surface.
  **This is not yet verified in this repo**; treat it as a hypothesis, not a fact.

## Constraints that cannot move

- One live run per process.
- Transcript rows are persisted display entries; raw event lines never touch disk.
- No secret, PIN or key material is written to a transcript or a log.
- The PIN gate and the local-only posture must not regress.
- No new dependency without a stated reason and a named alternative that was rejected.

## Non-goals

- Multiple simultaneous runs.
- A general permission-approval UI. This is about questions the model asks, not tool
  permissions.
- Replacing or bypassing the composer.
- Editing an answer after it is sent.

## Trade-off rows — take a position on each

1. **Fidelity vs scope.** Mirror opencode's TUI question flow exactly, or render the smallest
   answerable surface that works here.
2. **One click vs staged confirm.** An option click submits immediately, or selects and then
   needs an explicit confirm. The run is live either way.
3. **Free text vs options only.** Free text is where injection, secrets and persistence
   concentrate; options only is provably closed but excludes the real question that has no
   listed answer.
4. **Transport.** Keep `opencode run` and accept that questions cannot be answered; adopt
   `opencode serve`; or hold the process open and write to its stdin. Name the one you would
   take and what it costs.
5. **New surface vs existing rows.** A new bordered question card, or a composition of the
   transcript's existing row primitives. The contract warns against a card inside a panel.
6. **The answered state.** Fold to one quiet line — which loses the options not taken — or keep
   it expanded, which adds dead forms to the transcript.

## Evidence that would settle it

- A verified answer to: does opencode 2.3.0 emit a question event on `--format json`, and does
  `opencode serve` accept a reply? Until this exists, the transport row is a bet.
- A seeded screenshot at 375 and 900, light and dark, of the chosen surface in three states:
  pending, answered, and more options than fit.
- `dotnet test` green with the new path covered.

## What each seat returns

At most 400 words:

1. **Proposal** — the shape, why it wins, what it costs, the top risk, and what would change
   your mind.
2. **Positions** on the six trade-off rows above, one line each.
3. **The one thing you would cut**, and why.

Seat-specific additions:

- **Shape seat:** name the seam the change sits behind and what it defers.
- **Experience seat:** also draft the **experience thesis** — what an 8/10 must feel like, in
  observable terms (three-second read, first-run path, copy voice, one good moment, the
  friction to avoid).
- **Risk seat:** also give the top three failure modes with the single constraint that closes
  each, ranked.

No code, no implementation plan. Write to the output file named in your prompt only.
Do not read `chair.md`, `round1-*.md`, or any other council file.