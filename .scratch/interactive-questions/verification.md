# Verification — interactive-questions transport gate

**Date:** 2026-11-16. **Fixed point:** `35f1d42`. **Verdict:** the gate passes, with one
correction to the council's assumption and one experiment left.

## Verified

1. **The server is real.** `opencode serve` exists in v2.3.0 (`opencode --version`). Flags:
   `--hostname`, `--port`, `--cors`, `--service`, `--stdio`.
2. **Loopback by default.** Started with `opencode serve --port 4098` and *no* hostname; the
   listening socket was `127.0.0.1:4098`, not `0.0.0.0`. The default binds loopback.
3. **Authentication is required by default.** Unauthenticated `GET /api/info`,
   `/api/session` and `/openapi.json` all returned `401` with
   `{"_tag":"UnauthorizedError","message":"Authentication required"}`. Bootstrap is
   `POST /api/pair` → a short-lived, single-use code for `/auth/connect/{code}`. The CLI
   authenticates itself: `opencode api get /api/info --server <url>` succeeds without a token
   in the calling shell.
4. **The question primitive is a session form**, documented in the server's own
   `/openapi.json`:

   | Operation | Method and path | Purpose |
   | --- | --- | --- |
   | `form.list` | `GET /api/form` | list pending forms |
   | `session.form.list` | `GET /api/session/{id}/form` | list a session's forms |
   | `session.form.create` | `POST /api/session/{id}/form` | create a form |
   | `session.form.get` | `GET /api/session/{id}/form/{formID}` | read one |
   | `session.form.cancel` | `DELETE /api/session/{id}/form/{formID}` | cancel |
   | `session.form.reply` | `POST /api/session/{id}/form/{formID}/reply` | answer |

   Reply body: `{"answer": {<fieldID>: <value>}}` (`Form.Reply` → `Form.Answer`).

   Events: **`form.created`, `form.replied`, `form.cancelled`.**

5. **A form is richer than question-plus-options.** Field types in the contract:
   `Form.StringField`, `Form.NumberField`, `Form.IntegerField`, `Form.BooleanField`,
   `Form.MultiselectField`, `Form.Option`. `Form.Detail`, `Form.State` and `Form.When` also
   exist.
6. **Permissions are a separate channel.** `permission.asked`, `permission.replied`, and
   `POST /api/session/{id}/permission/{requestID}/reply`. A form is not a permission request.
7. **`--stdio` exists** as a serve flag.

## Not verified

- **A live `form.created` round-trip.** The contract is captured; no event was observed, because
  that needs a model turn that asks a question.
- **`--stdio` semantics.** Does it speak the same API over stdio with no listener at all? If it
  does, it is strictly better than a port: no bind, no pairing, no second network surface.
- **Disconnect and race behaviour.** `session.form.reply` and `session.form.cancel` both declare
  `409`, which suggests a consumed/cancelled form is refused rather than raced — but that is an
  inference from the schema.
- **How the app obtains and stores a pairing credential.** `opencode api --server` does it
  automatically; a Blazor app would have to reproduce that handshake and hold a secret.

## Consequences

- **Transport row resolved:** `opencode serve`, not stdin. The council was right for the wrong
  reason — the 4–0 was intuition; this is evidence.
- **Surface row needs narrowing:** options-only is a *subset* of a typed form. v1 must decide
  which field types it renders and what it does with the rest. The shape seat's round-2
  footnote — "grouped events would strain a Trace" — is now the main open design question.
- **Consent de-risked:** because permissions have their own channel, the app can refuse to
  render permission requests in the question row, which makes "an answer is data, never an app
  action" enforceable rather than aspirational.
- **Cost confirmed:** a second process and a pairing credential are real. The posture is sound,
  but it is not free.
- **One experiment before the spec:** does `--stdio` remove the listener? If yes, the entire
  pairing/bind question disappears.

## Method

`opencode --version`; `opencode serve --help`; a loopback server on 4099 with `--hostname`,
and one on 4098 without, to test the default bind; `/api/info` and `/openapi.json` fetched
unauthenticated (401) and through `opencode api --server` (200, 896 KB OpenAPI document);
ports inspected with `Get-NetTCPConnection`. Both servers were stopped and both ports confirmed
closed after the test. No prompts were sent and no model tokens were spent.

The captured OpenAPI document is not committed: it is 896 KB of generated schema and it changes
with the server version. Re-capture it with
`opencode api get /openapi.json --server <url> > openapi.json` when the contract matters.

## Addendum — `--stdio` experiment (2026-11-16)

Run after the gate against the same v2.3.0 binary. No model tokens were spent. This does not
re-open the gate; it settles the one experiment the gate left open.

### Result

1. **`--stdio` is not a stdio protocol.** It starts the same loopback HTTP API and writes one
   line to stdout: `{"url":"http://127.0.0.1:<port>"}`. Nothing is read from stdin or framed
   on stdout; sending it a JSON-RPC line or an HTTP request produced no response.
2. **It honours `--port 0`.** An ephemeral port is chosen and reported in that JSON line, so
   the supervisor does not probe for a free port or race another server.
3. **It binds loopback.** The reported socket was `127.0.0.1:<port>`, and an unauthenticated
   `GET /api/info` still returned `401`.
4. **Its lifetime is the parent's pipe.** Closing the child's stdin exited it with code `0`
   and the listener died with it.
5. **`/api/event` works** as `text/event-stream` once authenticated, opening with
   `server.connected`.

### Credential

The pairing handshake is removable, but `--stdio` is not what removes it. In non-`--service`
mode `serve` takes its password from `OPENCODE_PASSWORD` (then `OPENCODE_SERVER_PASSWORD`,
then a random per-process value), and the transport is HTTP Basic with username `opencode`
— not the bearer token the pair flow returns.

Verified end to end: a child spawned with an app-generated `OPENCODE_PASSWORD` returned `200`
to `Basic opencode:<secret>`, and `GET /api/event` opened as SSE. The secret lived only in
the child's environment; it was not written to disk, shown to the user, or logged. It
supersedes the pair flow for a spawned child: no pairing code, no `service.json`, no global
credential.

### Consequences

- **The listener is not removed.** `--stdio` removes the fixed port, the port-probing race and
  the orphan child, not the second network surface. That surface is now a loopback listener
  owned by the app's own child and authenticated with a per-run secret.
- **The pairing credential is removed.** That was the expensive half of the "second
  process + credential" cost the gate confirmed.
- The transport row still resolves to `serve`; this only makes the supervisor and the
  credential cheaper and safer. The surface decision is unchanged.

## Addendum — live form round-trip (2026-11-16)

The gate could not observe a `form.created` because that needs a turn that asks a question.
This closes that gap **without model tokens** by creating forms through the server's own API on
a real `serve --stdio` child (create session, then `session.form.create`), while listening on
`GET /api/event`. The payload is server-generated and real; only the asker is the probe rather
than a model turn.

### Observed

1. `POST /api/session` with `{"title": "form probe"}` returns `200` and a session id; no prompt
   is needed.
2. `POST /api/session/{id}/form` with one `string` field carrying three options returns `200`
   with the same `Form.Info` shape the contract declares. `form.created` arrives on the event
   stream carrying exactly `{"form": Form.Info}`.
3. `POST .../reply` with `{"answer":{"colour":"violet"}}` returns `204`; `form.replied` arrives
   carrying `{"id","sessionID","answer":{"colour":"violet"}}`. `GET .../form/{id}` then reports
   `state: {"status":"answered","answer":{"colour":"violet"}}`.
4. A second reply returns `409 FormAlreadySettledError` with
   `"Form already settled: <formID>"`. The race is refused, not applied.
5. A boolean field given a string returns `400 FormInvalidAnswerError` with
   `"Expected boolean for form field: ok"`. The error names the field key.
6. `DELETE .../form/{id}?message=...` returns `204`; `form.cancelled` arrives carrying only
   `{"id","sessionID"}`. The message is not echoed in the event.
7. A pending form is returned by `GET /api/session/{id}/form` after the event stream is
   reconnected, so a missed `form.created` is recoverable by listing.
8. Field titles are optional and are not synthesised by the server; a field sent without
   `title` comes back without one. The row must fall back to the field `key`.

### Consequences for the row

- The **notice** for an unsupported form can name the required kind from `field.type` alone;
  nothing else is needed.
- A submitted answer can be confirmed from either the `204` or the subsequent `form.replied`.
  The adapter should treat `204` as acknowledgement, `form.replied` as the authoritative
  settled state, and `409`/`400` as terminal failure of that submit — never as "still pending".
- Restored pending forms must render closed: the list endpoint is the restore path and it
  carries no "this was live a moment ago" signal.

### Addendum — wire envelopes observed (2026-11-17)

A second probe, still without model tokens, captured the exact JSON shapes the client parses:

1. **No `event:` field.** Every SSE line is `data: {…}`; the event name is the JSON's `type`
   (`server.connected`, `form.created`, `form.cancelled`, …), and its payload is `data`.
   A parser that waits for `event:` frames would never fire.
2. **REST replies are enveloped.** `POST /api/session` answers `{"data":{…session…}}`,
   `POST /api/session/{id}/form` answers `{"data":{…form…}}`, so the client unwraps one
   `data` layer before reading a form.
3. `form.created` carries `data.form` (the same `Form.Info` the create call returned);
   `form.cancelled` carries only `{"id","sessionID"}`.
4. `DELETE` of a pending form makes `GET .../form/{id}` report
   `state: {"status":"cancelled"}`, so a recheck can settle a dismissed row.

## Addendum — serve producer probe (2026-11-17)

The producer is the missing half: the transport can answer a form, but `opencode run`
cancels the form before the app can see it. This probe settles whether the serve API can
replace the producer without a session migration. It spent no model tokens: `resume:false`
enqueues input without starting a turn.

### Verified

1. **The caller can choose the session id.** `POST /api/session` accepts
   `{"id": "ses_…"}` (`Session.ID` is `^ses`). A created probe id was returned unchanged by
   `GET /api/session/{id}` and appeared in `GET /api/session`. The app can therefore keep its
   own ids, its session files and its `--session` identity.
2. **The prompt endpoint exists and is complete.** `POST /api/session/{id}/prompt` requires
   `text` and accepts `files`, `agents`, `skills`, `metadata`, `delivery` and `resume`. With
   `resume:false` it returns `200` with a `Session.Inbox.User` item and starts no turn.
3. **Agent and model are separate selectors.** `POST /api/session/{id}/agent` takes
   `{"agent": …}` and `POST /api/session/{id}/model` takes
   `{"model": {"providerID", "id", "variant"}}`; both return `204`. They are the server-side
   equivalent of `run --agent` / `--model`.
4. **The serve process sees CLI-created sessions.** `GET /api/session/{id}` and
   `GET /api/session/{id}/message` returned the existing `ses_04e4…` run (13 messages), so a
   producer switch does not split history or require importing transcripts.
5. **The event contract is richer than `run --format json`.** It includes
   `session.text.started/delta/ended`, `session.reasoning.*`, `session.tool.input.*`,
   `session.tool.called/progress/success/failed`, `session.step.started/ended/failed`, and the
   already-used `form.*` and `permission.*`. The 100-variant `V2Event` union has **no
   `session.status` member** (the `SessionStatusUpdated` schema exists but nothing in the union
   uses it); the terminal vocabulary is `session.idle` plus
   `session.execution.succeeded|failed|interrupted`, and the live turn confirmed
   `execution.succeeded` is the one a plain turn emits.
6. **ACP is a different vocabulary.** The binary exposes `session/new`, `session/load`,
   `session/prompt`, `session/update`, `session/request_permission`, `session/steering`,
   `elicitation/create` and `elicitation/complete`. It has no native form vocabulary; generic
   input is a JSON-Schema elicitation, not `Form.Info`.

### Consequences

- **No session migration.** The adapter can create or resume the app's existing `ses_…` id,
  so `SessionCoordinator`, `JsonSessionStore` and the transcript stay authoritative.
- **The producer is a prompt/stream mapping, not a new store.** `OpenCodeServe`,
  `OpenCodeFormClient`, `QuestionChannel` and the form row are already the hard parts; the
  adapter adds session creation, agent/model selection, prompt, interrupt and event mapping.
- **Serve is the primary route; ACP is a later interoperability adapter.** ACP is attractive
  for Zed/editor clients, but adopting it first would trade the app's typed form contract for
  a lossy elicitation translation and still require an event adapter.
- **`resume:false` is a free test hook.** The adapter can prove session creation, queueing and
  error mapping in tests without spending tokens; a real turn is needed only for the final
  end-to-end evidence.

### Method

`opencode serve --port 4102` with a generated per-process password held only in the child's
environment; `opencode api` (which inherits it) for requests, so no credential was written to
disk or echoed. `GET /openapi.json` (v2.3.0) supplied the endpoint and schema facts. The probe
server was stopped after each check. No prompt was executed; the only prompt call used
`resume:false`.

## The producer turn (2026-11-17)

One approved live turn through the real code — `ServeSupervisor` → `ServeRunRunner` — in the repo
folder, with `surplus/deepseek-v4.1-flash-1017` passed explicitly, message "Reply with exactly: ok".
Recorded verbatim at `%TEMP%\climb-ui\serve-turn\report.txt`; the probe file was deleted after it
ran.

**Order actually served** (frames with our `sessionID`, in arrival order):

```
session.created            ← POST /api/session with the caller's id succeeded
session.inbox.enqueued     ← the prompt was durably admitted
session.execution.started  ← the turn is bracketed; this is what arms the mapper
session.inbox.delivered
session.step.started       agent "build", model surplus/deepseek-v4.1-flash-1017
session.step.first-token
session.text.started / delta … / ended   ended carries text "ok"
session.step.streamed
session.step.ended         finish "stop", cost 0, tokens 7956 in / 3 out, cache read 256
session.usage.updated      same numbers again
session.execution.succeeded
```

**What that settled**

- `resume` may be omitted: the prompt was admitted and the loop ran, confirming the contract's
  "unless `resume` is false". The body sent `{"text": …}` and nothing else.
- `session.execution.started` precedes every content event, so arming on it is safe, and every
  terminal event arrives *after* it: the "ignore a replayed terminal" guard never discarded this
  turn's end.
- `session.idle` was **not** emitted for this turn; `session.execution.succeeded` was. Both are
  handled, and the armed guard keeps a stale `idle` from ending a run.
- `text.ended.text` is the complete block (`"ok"`), so one line per block is lossless.
- `step.ended` carries `cost` and `tokens{input,output,reasoning,cache{read,write}}` in exactly the
  shape `OpenCodeMetrics`/`RunMetrics` already sums; the mapped `step_finish` line summed to the
  same totals the server reported.
- `session.usage.updated` repeats the same usage; mapping it too would double-count, so it is
  dropped.
- A failed step and the failed execution it causes carry the same message. The first live run
  proved the naive mapping would print one error row twice; the mapper now remembers it already
  printed that failure and the execution's end adds no second row.

**Not proved by this turn:** a `form.created` raised during the run and answered through the shared
child. Raising a form needs the model to call the form tool, which this prompt deliberately avoids.
The single-child invariant is covered by `ServeProducerWiringTests` (a borrowed child is never
disposed), and form create/reply/cancel against a real child are already covered by the guarded
live tests in `OpenCodeFormClient`/`QuestionChannel`. The end-to-end form path needs a run that
happens to ask a question; it is not simulated. (The form turn below settles it live.)

## The form turn (2026-11-17)

One approved live turn with `Climber__Questions=serve`, against a scratch folder holding
`Alpha.cs` and `Beta.cs`. The prompt forced the installed server's own `question` tool: one
question, header "Which file?", two options "Alpha.cs" and "Beta.cs", nothing else until an
answer, then one sentence naming the chosen file.

**What the host log shows, in order:**

```
[18:12:27] $ opencode serve --stdio · POST /api/session/ses_9010…/prompt "Use the question tool…"
[18:12:31] {"type":"tool_use","part":{"tool":"question","state":{"status":"running",…}}}
[18:12:31] {"type":"climber.question",…"title":"Which of these two files should I edit?",
            "fieldKey":"q0","kind":"Choice","status":"Pending"}
[18:13:06] {"type":"climber.question",…"chosenValue":null,"status":"Submitting"}
[18:13:06] {"type":"climber.question",…"chosenValue":"Alpha.cs","status":"Answered"}
[18:13:06] {"type":"tool_use","part":{"tool":"question","state":{"status":"completed",
            "output":"User has answered your questions: \"Which of these two files should I
            edit?\"=\"Alpha.cs\". You can now continue with the user's answers in mind."}}}
[18:13:07] {"type":"text","part":{"text":"You chose Alpha.cs."}}
[18:13:07] ===== Completed =====
```

**Settled by this turn**

- A served run's `question` tool call becomes a real `form.created` on the shared child, and
  the channel attaches: no `[questions unavailable: …]` line, no `non-interactive` copy, no
  `cancelled · no answer sent`.
- The row's title is the field `description` (the complete question), not the form title
  `Questions` — the `FieldDisplay` precedence works against a real payload.
- A UI answer reaches opencode: `204`, then the authoritative `form.replied`, then the tool
  result the model sees, then the run continues and completes instead of cancelling.
- The model names the chosen file, so the answer that crossed the wire is the one the reader
  clicked (`Alpha.cs`, the first option).

**How the answer was given, exactly.** A click in the headed dev window, 35 s after the row
appeared. The headless probe that was supposed to click it never observed a `pending` row: its
`[data-slot="question"][data-state="pending"]` wait timed out even though the row was live (the
host log proves the state) and the same row rendered answered on that page once the run ended.
The probe is therefore **not** evidence for the click, and its timeout is not evidence of a
product defect — but it is unexplained, so it is recorded rather than dismissed. The screenshot
in this folder of the completed run (`%TEMP%\climb-ui\form-proof-shots\99-failure.png`) shows
`answered: Alpha.cs`, no pending-form copy, and `You chose Alpha.cs.`; the live *pending* row's
appearance is the seeded capture listed in `evidence.md`, not a live screenshot.

**What the first attempt found (and fixed).** The same turn run before these fixes failed: the
first form list returned `404` for a session the server had not surfaced yet, the channel threw,
and the transcript said `[questions unavailable: the question channel did not attach
(HttpRequestException)]` while the form sat unanswerable. Two fixes followed, each with a free
test: `OpenCodeFormClient.ListAsync` treats `404` as an empty list
(`ListOfASessionThatDoesNotExistYetIsEmpty`), and `QuestionChannel` reconciles again when the
event stream opens, closing the list/open race (`TheLiveStreamListsFormsCreatedBeforeItOpened`).

**Decision (2026-11-17).** With the loop proved, the shipped `Climber:Questions` in
`appsettings.json` is now `serve`, so a question is answerable by default. The parse is unchanged:
an unset, empty or unrecognised value is still `off`, so removing the key is a safe revert and a
typo still cannot start a second process. README's settings table and the spec's producer line
carry the new default.

**Still open**

- A channel that fails to attach *after* the child started leaves a form unanswerable and the
  run waiting; by spec the run is not failed. Not observed live, not fixed.
- The probe's pending-state blindness above. The next automated attempt should poll on a timer
  (`waitForFunction(…, { polling: 250 })`) and assert on the row element it captured, instead of
  relying on default `raf` polling and a fresh `querySelector`.

## After the first gate (7/10)

The gate named four flaws and one minor. Each is fixed, and each now has a free test that fails if
it regresses:

1. **A server-side interrupt said "Stopped by user".** `ServeEventMapper` now keeps
   `session.execution.interrupted.reason`; `ServeRunRunner` throws `ServeInterruptedException` for
   `shutdown|superseded|inactivity` and leaves `user` as a stop; `RunService` fails the run with
   `the run was interrupted (<reason>)` and no fallback offer. Tests:
   `ServeEventMapperTests.AServerSideInterruptKeepsItsReason` and
   `ServeRunServiceTests.AServerInterruptFailsTheRunAndNamesTheServer` /
   `ServeRunRunnerTests.AServerSideInterruptFailsTheRunAndKeepsTheReason`.
2. **A failed child start could start a second child.** `StartQuestionsAsync` now returns null when
   the run has no server, so the only `ServeSupervisor.StartAsync` a serve run can reach is the
   producer's. Test: `QuestionRunServiceTests.AChildThatNeverStartedGetsNoChannelAndOneLine`
   (the wiring half is `ServeProducerWiringTests`).
3. **A false attach line.** A channel that cannot attach now says
   `[questions unavailable: the question channel did not attach (<exception type>)` — the server
   did start, and the loopback address is not repeated in the transcript. Tests:
   `QuestionRunServiceTests.AFailingChannelIsOneLineAndTheRunStillCompletes` and
   `QuestionRunServiceTests.AChannelFailureLineNeverCarriesTheLoopbackAddress`.
4. **A different execution message could be swallowed.** The duplicate guard now compares the
   message instead of remembering "a row was printed". Test:
   `ServeEventMapperTests.AFailedStepWithADifferentMessageAndExecutionPrintBothRows`.
   The run's band also repeats the server's own failure words in serve mode instead of
   `opencode exited with code 1` (no process exited). Test:
   `ServeRunServiceTests.AServeFailureBandsTheServersOwnWordsNotAnExitCode`.
5. **Minor — `step.ended.files` was dropped.** The mapper now emits the CLI's `session.diff` row,
   one per step that lists files. Test:
   `ServeEventMapperTests.AStepThatChangedFilesPrintsTheFilesRow`.

The run loop is also drivable in `serve` mode by tests now: `RunService` takes an injectable
`Func<ServeChild, RunOptions, ICommandRunner>`, so `ServeRunServiceTests` exercises the real
serve branch (one child, shared, disposed once) without spawning a process. The live turn above
still stands as the only end-to-end evidence; none of these fixes needed another paid turn.