# Spec — interactive model questions

**Status:** approved for lanes 2–3. **Date:** 2026-11-16. **Fixed point:** `23be0ba`.
**Council:** [council.md](council.md). **Verification:** [verification.md](verification.md).
**Plan:** [plan.md](plan.md).

## Objective

The artifact a person experiences: a **question row in the ClimberWeb transcript** — the run
asks, the reader answers with one click or Enter, the row folds to a one-line record that opens
on demand.

## Settled decisions

- **Transport:** a spawned `opencode serve --stdio --port 0` child behind one
  emit/submit/cancel seam. The child prints `{"url":"http://127.0.0.1:<ephemeral>"}` on stdout;
  closing stdin ends it. HTTP Basic, username `opencode`, password = a per-run random secret
  passed only in the child's `OPENCODE_PASSWORD` and held in memory. No pairing code, no global
  config secret, no secret in a log, transcript or persisted record.
- **Answerable shapes:** exactly one field, either `string` with a non-empty `options` list, or
  `boolean` (rendered as the server's own `Yes`/`No`, values `true`/`false`). One click; the
  pointer and keyboard paths share one commit.
- **Everything else degrades:** a closed, read-only notice naming the kind of answer required,
  with one non-answer action `dismiss` → `session.form.cancel`. Never a dead form, never a stuck
  run.
- **Consent:** a form is not a permission. `permission.asked` never reaches the row. An answer is
  data to the model; no click executes a filesystem, tool or app action.
- **Producer:** `opencode run --format json` remains the transcript producer for now. The serve
  path is wired behind `Climber:Questions = off | serve`, default `off`. Why: the served event
  vocabulary (`message.part.updated`, `message.updated`, `session.idle`) has no mapping in
  `LogFormatter`; flipping every run onto it would turn every row of every transcript into raw
  JSON to ship a rare row. The flag makes the producer migration a separate, reversible change
  once the mapping is proved.
- **Non-goals:** multiple simultaneous runs; a general permission UI; replacing or bypassing the
  composer; editing an answer after send.

## The row

| State | Row | Interactive |
| --- | --- | --- |
| `Pending` (choice) | `asks:` + field title, option rows, hint, escape line | options, Enter/Space |
| `Pending` (boolean) | `asks:` + title, `Yes` / `No` | same |
| `Submitting` | same row, options disabled, `aria-busy="true"`, `sending…` | none |
| `Answered` | folded: `answered: <label>`; open: title, every option, taken one marked | disclosure |
| `Cancelled` | `cancelled · no answer sent` | none |
| `Unsupported` | `asks:` + title, `<kind> answer required`, `dismiss` | dismiss |
| `Refused` | `that answer was refused` + `check` | check |
| `Unconfirmed` | `submit unconfirmed` + `check` | check |
| `Stale` / restored pending | `no answer sent`, closed | none |
| `Unavailable` | `questions unavailable` | none |

Composition: `Trace` / `TraceLine` / `TraceTitle` / `TraceCollapsible` / `Button`. No new card,
no new border shape. The pending row is the only thing below the status band at answer weight:
a 2px leading edge in `border-primary`, the model's question verbatim in sans at answer weight,
options as rows, hint and escape at the 11px micro level.

## Copy

| Slot | Exact string |
| --- | --- |
| prefix | `asks:` |
| escape (pending only) | `none fit? answer via the composer.` |
| hint (pending only) | `↑↓ move · enter commits` |
| answered folded | `answered: <option label verbatim>` |
| taken option mark | `→ <label>` + `sr-only` `chosen` |
| cancelled | `cancelled · no answer sent` |
| restored pending | `no answer sent` |
| kind: number / integer | `number answer required` / `whole-number answer required` |
| kind: string, no options | `text answer required` |
| kind: multiselect | `several answers required` |
| kind: external | `a link is required` |
| kind: 2+ fields | `more than one answer required` |
| kind: unknown | `an unsupported answer is required` |
| dismiss | `dismiss` |
| submitting | `sending…` |
| refused / unconfirmed | `that answer was refused` / `submit unconfirmed` + `check` |
| channel dead | `questions unavailable` |
| status band while pending | `awaiting answer` (view override only) |
| a11y announcement | `question from the run · focus moved to <first label>` |

The question text is the model's, verbatim. App copy is terminal-quiet. No option label is
truncated; no string contains `permission`, `allow` or `deny`.

## Seam contract

`ClimberWeb/Services/QuestionContract.cs`:

```csharp
public enum QuestionKind { Choice, Boolean, Unsupported }
public enum QuestionStatus { Pending, Submitting, Answered, Cancelled, Unsupported, Refused, Unconfirmed, Stale, Unavailable }
public sealed record QuestionOption(string Value, string Label, string? Description);
public sealed record QuestionState(
    string FormId, string SessionId, string Title, string FieldKey, QuestionKind Kind,
    IReadOnlyList<QuestionOption> Options, string? ChosenValue, QuestionStatus Status,
    string? Notice, string? KindLabel);
public enum QuestionSubmitOutcome { Accepted, AlreadySettled, InvalidAnswer, Unconfirmed, Disconnected }
```

`IQuestionChannel` (Services) owns the wire, and is the only route from a rendered option to
the world:

```csharp
public interface IQuestionChannel : IAsyncDisposable
{
    event Action<QuestionState>? Changed;
    Task StartAsync(string sessionId, string targetFolder, CancellationToken ct);
    Task<QuestionSubmitOutcome> SubmitAsync(string formId, string fieldKey, string value);
    Task<QuestionSubmitOutcome> CancelAsync(string formId);
    Task<QuestionSubmitOutcome> RecheckAsync(string formId);
}
```

`IRunService` gains exactly three members: the two actions `AnswerQuestionAsync(formId, fieldKey, value)`
and `DismissQuestionAsync(formId)`, plus the read-only `CheckQuestionAsync(formId)` behind the row's
`check`. There is no `Allow`, `Approve`, `Execute` or arbitrary-promise route on either interface.

**Support matrix (`QuestionSupport.Classify`, enforced where the reply is sent):** one field,
`type == "string" && options.Count > 0` → `Choice`; `type == "boolean"` → `Boolean`; anything
else → `Unsupported` + kind label. A choice reply sends the option **`value`**, never the label
— even when `custom: true`, because free text stays the composer's job. An empty field title
falls back to `key`.

## Transport supervision

- Spawn `opencode serve --stdio --port 0` in the run folder, stdin/stdout/stderr redirected.
  Generate the password with `RandomNumberGenerator`; it is reachable only from the
  `Authorization` header and unreachable from `ToString()` or any log line.
- Read stdout until the URL line parses; 10 s budget, then kill the tree.
- `HttpClient` per child with `Authorization: Basic base64("opencode:" + password)`.
- `GET /api/event` with `ResponseHeadersRead` and an infinite timeout; parse `data:` frames.
  Every frame's JSON carries `type` and `data` (opencode leaves the SSE `event:` field empty),
  and every REST payload is wrapped in `{"data":…}`. Dispatch an **allowlist**:
  `form.created`, `form.replied`, `form.cancelled`. Everything else, including
  `permission.asked`, is dropped before any state or line.
- On every connect, `GET /api/session/{id}/form`. A pending form not yet on screen renders
  `Stale` (`no answer sent`); a form already shown keeps its state, so a brief reconnect never
  strands a live question.
- Reconnect 250 ms → 2 s → 5 s cap; five failures in 60 s → `Unavailable`; a silent 60 s stream
  reconnects silently.
- Lifetime is the run: start only when `Climber:Questions == serve` and a session id exists;
  dispose on any terminal status, on `Stop()` and at host shutdown. Dispose closes stdin, waits
  2 s, then kills the tree.

## Failure and timeout

| Case | Behaviour |
| --- | --- |
| child fails to start | one line `[questions unavailable: the run's server did not start]`; the run continues |
| channel throws | mapped to `Unavailable`; one line; run status untouched |
| reply 204 | `Accepted`; `form.replied` is authoritative |
| reply 409 | `AlreadySettled` → `Answered` if a value is known, else `Cancelled`; never still pending |
| reply 400 | `Refused` + `check`; the server message goes in `title`, not visible copy |
| submit > 10 s | `Unconfirmed`; no automatic retry; `check` = `GET form/{id}` |
| cancel fails twice | `dismiss failed · retry`; the pending row is never silently dropped |
| permission frame | dropped; no state, line or counter |

## Dev and test seam

The row must be testable without a model turn and without tokens, and the same seam must serve
the future live-app harness. This pass built the second half only:

- **Built now — a committed replay seed.** `ClimberWeb.Tests/Fixtures/questions.jsonl` is replayed
  through the real `LogFormatter` and `LogGrouper` by the existing `CLIMBER_SEED` host, so the row
  can be studied in every state it has (pending, answered, cancelled, stale, unsupported) without
  a model turn, a channel or a token. `PlaybackFixtureTests` pins the states it must reach, so the
  evidence cannot rot silently.
- **Deferred — the runtime stimulus.** `IQuestionStimulus` (dev-only `Emit(QuestionState)` /
  `Reset()`, registered only under `Development`) is lane 6, not this pass: a question that
  arrives mid-turn, answered against a fake channel so the real `AnswerQuestionAsync` → fold path
  runs and the answer is recorded rather than sent. A replay cannot exercise that, and building
  it now would add a dev-only surface before the live-app lane that needs it exists.
- No HTTP test route and no MCP server in this pass. The seam is runtime-callable by design, so
  the future live-app lane can add a dev-only endpoint and an MCP sidecar without changing the
  row or the channel.
- **Consequence, recorded as evidence, not as a flaw:** in the replay host a click on an option
  is inert. `PlaybackRunService.AnswerQuestionAsync` returns `Disconnected` and nothing publishes
  `Submitting`, so the row only changes when a real channel is attached. The submit path is
  covered by the transport gate and the live round-trip in `verification.md`.
- **Future lane (not built now):** `dotnet watch` keeps the app up; a dev-only control endpoint
  pushes questions and activity; an MCP sidecar exposes `emit_question`, `answer`,
  `read_transcript`, `screenshot`. ACP is not part of this: it is the app↔opencode transport,
  not the dev harness.

## Persistence

- `StoredEntry` gains an optional `StoredQuestion` (form id, status, title, field key, kind,
  options, chosen value, notice). Only display data is persisted — never the URL, password,
  header or raw frame.
- Restore: `Answered` renders closed; a restored pending or `Stale` renders `no answer sent`,
  closed and non-interactive. A pending and a settled line both persist; the grouper collapses
  to the last line per form id so a transcript never shows the same question twice.

## Floor and target

**Target score: 8.** The level-8 traits and the gate are quoted in
`docs/agents/hillclimb-rubric.md`. Floor: `dotnet build`, `dotnet test`, and the free harnesses
(`theme-nav`, `security`, `providers`); `polish.mjs` is supporting evidence, `surfaces.mjs` is
not run without approval. A red floor cannot score 8 or ship.

## Off-limits

`.env`, `*.key`, `*.pem`, `*.local.json` (including `appsettings.local.json` under `bin/`),
home config/data stores, any auth or credential file. The per-run password is generated, never
read from configuration, and never written anywhere.