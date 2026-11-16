### 1. Risk posture

Trust boundary: the human reader is the only trusted party; everything the `opencode`
subprocess emits — including the question text, its options, and whatever caused it to ask —
is untrusted content, since it can be shaped by files or web pages the agent read. Transport:
`OpenCodeRun.cs` only builds a one-shot, non-interactive `opencode run ... --format json`
invocation; it defines no channel for writing an answer back into a running process. An 8/10
must not invent that channel implicitly — it must be explicit, single-writer, and gated by
the same PIN session as everything else. What must never happen: a question must never cause
an action by merely being rendered or answered (no auto-exec on selection), and an answer must
never be attributable to the reader unless it traveled over an authenticated, server-verified
session.

### 2. Three highest-value risks

1. **Session-id hijack over the LAN.** `spec.md` Finding 4 records that the PIN/session id
   travel in cleartext (`Program.cs:63`; `App.razor:103-120`), unresolved. A LAN sniffer could
   replay the session id and submit a fabricated answer, steering a live agent run. Closing
   constraint: the answer endpoint must not ship while Finding 4 is open, or must enforce
   loopback-only for any run with a pending question.
2. **Consent laundering via the question UI.** The question/options text is agent-chosen and
   can be shaped by content the agent read, so a malicious doc could phrase a destructive
   confirmation as an innocuous choice. Closing constraint: render the question and option
   strings verbatim, unsummarized, and persist the exact question/answer pair immutably in the
   transcript so every approval is auditable after the fact.
3. **Stuck or duplicated turn.** No stdin-write path exists today; a write to an exited or
   already-advanced process can hang silently or double-submit on reconnect. Closing
   constraint: each question gets a single-use token — first write wins, late or failed writes
   surface as a visible transcript error, never a silent stall.

### 3. The one thing you would cut

Cut the free-text answer. It is the one part of the proposal with no bound: it hands raw,
reader-authored text straight into a running agent that already has filesystem and git access
(Finding 6, unresolved), and that text is logged verbatim at `Information` (Finding 3,
`RunService.cs:241`). Options alone keep every answer a known, pre-declared string the UI can
render exactly and audit exactly; free text turns the question feature into an unreviewed
command channel.
