### 1. Risk posture

Trust boundary: the `opencode` child's stdout is untrusted — its "question" and "option" strings are model-mediated text derived from repo files, which may carry injected instructions. The browser is semi-trusted: PIN-gated, may bind the LAN, cleartext. The app is the sole authority allowed to turn an answer into process input.

Transport: `OpenCodeRun.Request` is a one-shot, non-interactive invocation — prompt in `argv`, `--format json` events one way out, no stdin channel (`ClimberWeb/Services/OpenCodeRun.cs:41-62`). The proposal silently demands a second transport; an 8/10 names it before a question card ships.

Constraints that do not move:
- Question and option text render as text only. No `MarkupString`/`Html.Raw` — that would be the app's first XSS sink, in a repo whose security pass certifies there are none.
- An answer is data sent to a model, never consent to an app action.
- Exactly one answer per pending question; none accepted when none is pending; none ever synthesized.
- A pending question expires; expiry ends the run and says so.

### 2. Three highest-value risks

1. **Invented protocol.** `run --format json` documents no question event and no answer channel. The obvious build — parse an event, write stdin, wait — deadlocks: the child waits for input the app never delivered, the app waits for an event that never comes. Frozen transcript, no error, leaked process. Constraint: the only answer path is a documented one; if none exists, resume via `--session` and never hold a process open for a human.

2. **Answer as approval.** A repo file steers the agent to ask "Allow access to C:\Users\…?" A tidy options card teaches the reader to click yes, and a selection becomes authority over a filesystem-writing agent. Constraint: an answer is an index into the offered options or bounded text — it can never be parsed as an app decision.

3. **Routing by client-supplied session id.** Finding 4 in `.scratch/security-pass/spec.md`: the session id is client-supplied and travels cleartext. Keying delivery to it lets a stale or second tab answer a run it is not watching, or replay an answer after the run advanced. Constraint: pending questions key to a server-issued, single-use, run-scoped handle; the session id is never a capability.

### 3. The one thing you would cut

Free text. It is the only element that cannot be validated, and the only one carrying arbitrary user content into persisted JSON, `Information` logs (Finding 3), and a model provider. Selection-only leaves an integer that is trivial to validate, replay-proof and non-leaky. Readers wanting prose already have the prompt box.