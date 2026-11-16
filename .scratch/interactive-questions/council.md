# Council — interactive model questions

**Status:** deliberation complete; two rows escalated to the user.
**Date:** 2026-11-16. **Fixed point:** `35f1d42`.
**Brief:** [brief.md](brief.md). **Chair's sealed proposal:** [chair.md](chair.md).

## Question

Should ClimberWeb render opencode's interactive questions as answerable UI in the transcript —
and if so, in what shape, over what transport, and at what cost to the run pipeline?

## Constraints and non-goals

Constraints: one live run per process; transcript rows are persisted display entries and raw
event lines never touch disk; no secret, PIN or key material in a transcript or log; the PIN
gate and loopback-only posture must not regress; no new dependency without a named rejected
alternative.

Non-goals: multiple simultaneous runs; a general permission-approval UI; replacing or bypassing
the composer; editing an answer after it is sent.

## Proposals

Round 1 was independent. Full text, unedited:

- [Chair](chair.md) — rows, not a card; `opencode serve` only if verified; one click for listed
  options; free text demoted to a trailing "other…"; fold to one line; if the transport is
  unverified, ship only a visible "the run asked a question and was cancelled" row.
- [Shape](round1-shape.md) — one `Trace` summary line with an `aria-pressed` button group; one
  narrow emit/submit/cancel seam with a CLI adapter today and a server adapter later; free
  text deferred; `opencode serve` narrowly, stdin rejected as a fabricated protocol.
- [Experience](round1-experience.md) — a `Message` turn composed of `Trace` parts, accent on the
  pending question only; staged select-then-confirm; options only, with the composer as the
  documented escape; fold to a quiet answered line.
- [Risk](round1-risk.md) — keep the feature off `opencode run`; a supervised, loopback-only
  serve child with a hard timeout and guaranteed kill; staged confirm; options only, enforced
  where the reply is sent; fold, because an expanded dead form is exposure at rest.

The only round-1 majority the council produced by agreement rather than argument was
`opencode serve` (4–0). Round 2 attacked it.

## Critiques — what round 2 changed

Full text: [shape](round2-shape.md), [experience](round2-experience.md), [risk](round2-risk.md).

1. **"Verified" was a happy-path bar.** The shape seat attacked the 4–0 on its own side: all
   four seats applied one test — *undocumented → fail* — and failed stdin for a missing
   document while betting the pipeline on a path with the same document missing. Serve survived
   on tone, not evidence. The risk seat tightened the bar: **verified-and-safe-by-default**,
   which also has to show the bind address, the auth model against this app's PIN, and that a
   reply on disconnect fails safely rather than racing. The surviving agreement is only: **no
   answering surface until a real 2.3.0 payload has been captured.**
2. **The fold was lossy, not tidy.** The experience seat revised its own position. Answers are
   un-editable by non-goal, so a one-line `answered: option B` leaves no way to audit *why* a
   run diverged. Fold, yes — but the folded line must **expand on demand**, showing the
   question and every option with the taken one marked. That is a collapsed record, not a dead
   form.
3. **Consent laundering is the top failure mode.** The risk seat retracted "options-only is
   provably closed": a question whose *options* are consent actions — "Allow access to
   `C:\Users\…`?" / Yes / No — turns an options-only surface into an unreviewed permission
   grant with no free text involved. **Non-negotiable: an answer, option or text, is data sent
   to the model, never an app action.** No UI path may execute a filesystem, tool or permission
   effect from an answer click.

## Experience thesis

What an 8/10 must feel like, in observable terms:

- **Three-second read:** the pending question is the brightest thing below the status band —
  accent edge, sans question at answer weight, options as rows. Nothing else on screen changed
  weight.
- **First-run path:** the question appears mid-stream; focus moves to the first option and that
  is *stated*, not silent; arrows move, Enter commits; the row folds; focus returns to the
  composer. Keyboard only, no trap, under ten seconds.
- **Copy voice:** the question text is the model's, verbatim. App copy is terminal-quiet —
  `asks:` as a prefix, never "The assistant would like to know…". The no-fit row names the
  escape: *none fit? answer via the composer.*
- **One good moment:** answering collapses the row to `answered: option B` and the machinery
  steps aside the instant it is no longer live.
- **Friction to avoid:** a modal; a re-render that resets a selection mid-stream (open state
  lives in C#); a restored pending question rendered as a live form.

## Value trade-off register

| Trade | What it buys | What it costs | Who feels it | Reversible? | Position | Strongest counter-argument |
| --- | --- | --- | --- | --- | --- | --- |
| Mirror the TUI vs smallest surface | familiarity for opencode users | a larger surface that must fail closed | every question | yes | **smallest** (4–0) | TUI parity is the reference; divergence is learned twice |
| One click vs staged confirm | two interactions become one | a misclick lands on a live, un-editable run | every question | no, once sent | **one click** (user decision, 2026-11-16), with the commit identical for pointer and keyboard | irreversibility, not frequency, is the structural argument |
| Free text vs options only | covers the question with no listed answer | secrets, injection and persistence in one channel | the rare hard question | no, once persisted | **options only in v1** (3–1), with the composer escape named in-row | a feature that fails on the hard question is a toy |
| `opencode serve` vs `opencode run` vs stdin | the only path that can answer at all | a second long-lived process, a second listener, a new failure mode | every run | yes, until shipped | **serve, verified-and-safe-by-default, behind the seam** (4–0); stdin rejected (4–0) | the whole feature may not justify a transport rewrite |
| New card vs existing rows | a visually distinct question | a fourth bordered shape, the "card inside a panel" drift | the transcript's rhythm | yes | **existing rows** (4–0) | a question should be louder than a tool row |
| Fold vs keep expanded | a scannable transcript | the options not taken | anyone auditing later | yes | **fold, with a disclosure** (revised in round 2) | collapsing loses the deliberation the answer came from |

## Options

| Option | Shape | Cost | Risk | Reversibility | What it buys |
| --- | --- | --- | --- | --- | --- |
| **A. Visible cancellation only** | today's message becomes a transcript row; no answering | hours | none new | total | legibility: the run stopped for input, not silence |
| **B. Serve behind the seam** (recommended) | verified serve child → emit/submit/cancel seam → rows, options only, disclosure fold | the largest run-pipeline change since session persistence | a second listener, a second lifecycle, consent laundering | high until shipped; the seam keeps the UI | the feature as asked |
| **C. Serve + demoted free text** | B plus a trailing "other…" that reveals a field | B plus a validated free-text path | the risk seat's only working enforcement is defeated | same as B | the hard question with no listed answer |
| **D. Hold `run` open on stdin** | write to the child's stdin | cheapest to build | deadlock with a polite face: frozen transcript, no error, leaked child | none once it hangs | nothing that survives verification |

Options C and D were rejected: C 3–1, D 4–0.

## Recommendation

1. **Verify first, as a gate.** Capture one real 2.3.0 serve question payload, fields included,
   and confirm the bind address, the auth model against the PIN gate, and fail-safe behaviour on
   disconnect. Until that exists, no answering surface is built — not even against a seed, which
   would prove rendering and nothing about the protocol.
2. If the payload exists and is safe by default, build **Option B** behind one narrow
   emit/submit/cancel seam: a serve adapter, rows composed from existing primitives, options
   only, disclosure fold, and the consent constraint enforced where the answer is sent.
3. If it does not, ship **Option A** — the visible cancellation row — and stop. That is honest
   and cheap, and it makes the missing capability legible instead of silent.

## Dissent

- **Free text (chair, outvoted 3–1).** The chair held that an options-only feature fails on the
  one question that matters. The council accepted the diagnosis and rejected the remedy: the
  composer is the trusted path, so the row must *name* it rather than grow a second field. The
  chair's fallback position survives as the in-row copy requirement.
- **One click (chair and shape, against experience and risk).** Decided by the user on
  2026-11-16: one click, on the shape seat's condition that the commit is identical for pointer
  and keyboard. The dissent stands as a constraint, not a footnote — experience and risk argued
  that an answer consumed mid-run and un-editable by non-goal is irreversible, so the row must
  not offer two input paths with different physics.

## Escalated — the council cannot settle these

1. **One click or staged confirm?** _Settled by the user on 2026-11-16: one click_ — see Dissent.
2. **Is the transport rewrite worth it at all?** Still open. The shape seat escalated it as a
   value call, not a technical one: the feature may not justify a second process and a second
   listener even if verification passes. Verification informs it; it does not settle it.

## Decision

_Recorded 2026-11-16._

- **Answer semantics:** one click, with an identical commit for pointer and keyboard.
- **Transport:** `opencode serve`, behind the seam. Verified loopback-by-default and
  auth-required; Option A is no longer the fallback, because the primitive is documented.
- **Surface:** existing rows, options only, disclosure fold, and the consent constraint
  enforced where the answer is sent — narrowed further by the form schema below.
- **Next:** settle the field-type subset and whether `--stdio` removes the listener, then hand
  to `plan-architect` for the spec. The transport-rewrite value call stays open until the cost
  is known, which is now a real number rather than a guess.

## Verification outcome (2026-11-16)

Gate run at `35f1d42`; full record in [verification.md](verification.md).

- **Passed:** `opencode serve` is real, binds loopback by default, and requires auth
  (`401` unauthenticated, bootstrapped by a short-lived pairing code). The question primitive is
  a **session form** with `form.created` / `form.replied` / `form.cancelled` and a reply
  endpoint. The transport row resolves to serve; stdin stays rejected.
- **Corrected:** the primitive is richer than the council assumed. A form carries typed fields —
  `string`, `number`, `integer`, `boolean`, `multiselect`, `option` — not just
  question-plus-options. The shape seat's round-2 footnote about grouped events is now the main
  open design question, not a footnote.
- **De-risked:** permissions are a separate channel (`permission.asked` / `permission.replied`,
  their own reply endpoint), so a form is not a permission. The consent constraint becomes
  enforceable: the question row must refuse to render a permission request.
- **Still open:** a live `form.created` round-trip, `--stdio` semantics, and disconnect
  behaviour. If `--stdio` speaks the same API with no listener, it is the better transport and
  the pairing question disappears — one experiment settles it.

## Thread split

| Lane | Scope | Write scope | Depends on |
| --- | --- | --- | --- |
| 1. Verify (gate) | capture the serve payload; bind, auth, disconnect behaviour | `.scratch/interactive-questions/verification.md` only | — |
| 2. Transport | emit/submit/cancel seam, serve adapter, supervisor timeout and kill | `ClimberWeb/Services/` | 1 |
| 3. Surface | question row, option group, fold + disclosure, composer-escape copy | `ClimberWeb/Components/Ui/` | 1 (shape), 2 (live path) |
| 4. Persistence | question/answer rows in the session record; restored history renders closed | `ClimberWeb/Services/Session*`, `ClimberWeb/Components/` | 3 |
| 5. Evidence | fixture, twelve screenshots, keyboard read-through, floor | `scripts/ui/`, test project | 3, 4 |

Lanes 2 and 3 do not share files. Lane 4 is separable from transport. Lane 1 blocks everything:
without a captured payload the row's shape is a guess.

## Decision

_Pending. Filled in by the user, with the date._