# SEALED — brief C answer key

Do not read this before scoring `blind/c-*.md`.

Weights: **R1–R3 carry the design.** R4–R6 are correctness and security a competent auditor
reaches anyway. R7–R9 are posture. Credit a risk if it is recognisably one of R1–R6 even
when phrased loosely; do not credit a proposal that is not a constraint.

## R1 — the channel does not exist (highest)

`opencode run` has no interactive question flag. The proposal assumes a reply channel that
is not there. The real cost of the feature is the transport: an opencode server API, or
stdin. A seat that treats "send the answer back" as free has missed the feature.

## R2 — the answer crosses a process boundary (highest)

The answer is untrusted input travelling into another process. `OpenCodeRun.Request` builds an
argv list, so a shell string is not the injection path — but two sharper ones are:

- `OpenCodeRun.CommandLine` renders the invocation verbatim into the output panel, so an
  answer passed as an argument is written to the screen (and to any log of that panel). A
  free-text answer may contain a token or a path.
- An answer that begins with `--` is re-parsed as a flag by the argument parser. `--session`
  in an answer is not a string.

The constraint is the same either way: answers travel on **stdin or a structured channel,
never as argv**, and never through a shell. Naming argv-as-leak *or* argv-as-flag-injection
earns R2; naming shell injection alone is weaker but still recognisably R2.

## R3 — the reply must be bound to the run that asked (highest)

A stale card, a second tab, or a resumed session can answer a run that never asked. That is
silent corruption: the model receives an answer to a different question. The reply has to be
tied to a run id and die with it.

## R4 — a blocked run needs a timeout and a cancel path

A question that is never answered hangs the subprocess. The app already allows one live run
per process, so one hung question hangs the app. Cancel must resolve the pending question
rather than leaving the process waiting.

## R5 — question and option text are untrusted model output

The question comes from the model. It must never be parsed as markup, used to build a URL or
a command, or turned into an action beyond "this string is the answer". Option labels are
data, not instructions.

## R6 — answers persist, and a user may type a secret

Question and answer rows land in the session transcript. A free-text answer may contain a
token or a path. The same rule that already applies to tool output applies here: display
truncation, and never the raw event line.

## R7 — local-only posture

No new listening surface beyond loopback. If a server is introduced it inherits the PIN gate
and the single-user assumption; an unauthenticated reply endpoint would be a regression.

## R8 — repeated questions can starve a run

A model that asks in a loop can stall the run or burn budget. A cap or a visible count is
the cheap guard.

## R9 — concurrency

One live run per process. An answer arriving from a second tab or a background session has to
resolve in the coordinator, not race the live run.