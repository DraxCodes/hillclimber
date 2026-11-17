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