# Round 1 — Risk seat

## Proposal

Keep the question feature off `opencode run` entirely. If adopted, `opencode serve`
runs as a supervised child process, loopback-only, with a hard lifecycle: started
with the run, killed on timeout, disconnect, or the run's own completion — never
held open on an unbounded wait. Treat the serve question/reply API as an unverified
hypothesis (per the brief) and ship it behind a path that fails closed to today's
"cannot ask" message if the contract doesn't hold. Cost: a second long-lived
subprocess per live run, a new network surface that must inherit the existing PIN
boundary rather than invent its own, and a dependency on an API this repo hasn't
confirmed. Top risk: a sloppy loopback bind or auth inheritance on the serve
surface becomes an unauthenticated side door — the same class of exposure the
security pass just closed (Finding 1). What changes my mind: verified proof that
2.3.0's serve emits a real question event and accepts a race-free reply under one
live run; absent that, don't ship, keep "cancelled."

## Positions

1. Fidelity vs scope: smallest answerable surface — TUI parity buys little and widens what must fail closed.
2. One click vs staged confirm: staged confirm — a live, irreversible answer (no edit per non-goals) is the wrong place for a single slipped tap.
3. Free text vs options only: options only — free text is where secrets, injection and persistence risk all concentrate at once.
4. Transport: `opencode serve`, loopback-only, supervisor-enforced timeout and kill — never a bare stdin hold on `opencode run` (see failure #1).
5. New surface vs existing rows: compose from existing Trace/Message rows — a new card is one more thing that must fail closed, not just render.
6. Answered state: fold to one quiet line — an expanded dead form sitting in a persisted transcript is exposure at rest with no remaining purpose.

## The one thing I would cut

Free-text "Other" for v1. It's the single branch that touches secrets, prompt
injection, and plaintext persistence simultaneously, and the brief already names
it as the concentration point. Options-only is provably closed; free text is not.

## Top three failure modes, ranked

1. **Half-open process on an unbounded wait.** A subprocess (stdin-held `run`, or
   a serve connection) blocked on user input with no timeout turns "one live run
   per process" into a stuck-forever process with no cancel path. **Closes with:**
   every interactive channel carries a supervisor-enforced hard timeout and a
   guaranteed kill — never opencode's own behavior as the only backstop.
2. **Free text becomes the exfiltration/injection channel.** An answer field is
   the one path that can carry secrets, PII, or injected instructions straight
   into a persisted, cleartext transcript and back into the model. **Closes
   with:** options-only answers, enforced where the reply is sent, not just hidden
   in the UI.
3. **A new server surface regresses the local-only posture.** `opencode serve`
   adds a second listener; if it isn't loopback-only and gated behind the same
   PIN boundary, it reopens the exposure class the security pass just fixed.
   **Closes with:** the serve process and its reply endpoint are reachable only
   through the existing PIN-gated boundary, fail-closed the same way
   `ClimberBinding.Resolve` already does — never a directly reachable port.
