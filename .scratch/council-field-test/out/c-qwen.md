### 1. Risk posture

Trust ends at the server process. Answers come from a browser, over a transport the spec itself calls semi-trusted: plaintext HTTP on the LAN, session id in cleartext (`spec.md`, decision 3, finding 4). An answer is untrusted network input: one Blazor circuit reaching a live `opencode` process (`OpenCodeRun.cs`) editing a folder with destructive git reach. Must never happen: an answer from a client that never saw the question; two answers for one question; the run JSON diverging from what the process consumed.

### 2. Three highest-value risks

**1 — Unbound answer submission.** The app's first write path into a live agent. If submit carries only a run id and a string, any PIN holder — phone, stale tab, neighbour — can answer any pending question in any run: remote control of a process with filesystem and git reach. Constraint: each answer carries a server-issued, single-use token bound to one run and one exact question, checked against the unlocked session, refused once consumed.

**2 — Duplicate or stale answer.** Circuits reconnect, buttons double-fire, users leave a pending question and return. A second answer is consumed by whatever the process reads next: the run continues on an answer it never solicited while the JSON records something else — undetectable after the fact. Constraint: exactly-once, server-side: pending → answered → consumed, transitions atomic; late submissions error visibly, never buffered.

**3 — The invocation can't answer.** `OpenCodeRun.Request` builds one non-interactive `opencode run --format json`, no stdin. "Send back to the running process" means a permanent write channel on the agent, or `--session` resume where two processes hold one conversation and interleave writes to its JSON; absent both, a model-side prompt blocks forever while the transcript shows a hung run. Constraint: pick one — a pending-question deadline that kills the run, or resume that refuses a second process for a live session.

### 3. The one thing you would cut

Cut the free-text answer. Options are a closed set: render them, send back the choice id. Free text is the only shape where an answer is indistinguishable from a new prompt — arbitrary network text landing mid-run, logged and persisted verbatim like all tool output (`spec.md`, finding 3). The app already continues a conversation as an explicit turn: `OpenCodeRun.Request` takes a `sessionId`. Put free text there, consented per send. Risks 1 and 2 then shrink to token bookkeeping.
