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