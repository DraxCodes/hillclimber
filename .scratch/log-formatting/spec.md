# Spec: Readable output panel (structured JSON log rendering)

## Context

The dashboard's `[03] output` panel renders the run's live log feed: one
`.log-line` per entry in `ClimberState.Logs`. Two sources feed it:

- The loop's own messages, and `dotnet test` / `git` stdout, stamped as
  `[HH:mm:ss] <text>` by `HillClimberService.Log`.
- **`opencode run "<goal>" --format json`**, which emits one very long
  single-line JSON object per event. Each object becomes exactly one
  `.log-line`, so the panel fills with unreadable JSON blobs (`part.id`,
  `sessionID`, `messageID`, `metadata.rawInput`, and a `metadata.content`
  array that duplicates `part.state.output`).

The events are already individual `Logs` entries by the time the UI sees them
(`ProcessCommandRunner` invokes `onOutputLine` once per stdout line), so the fix
belongs in rendering, not in the service or the log feed.

The real schema (captured from `opencode run --format json`) is a flat object:

```json
{"type":"text","timestamp":…,"sessionID":"ses_…","part":{"id":"prt_…",
 "sessionID":"ses_…","messageID":"msg_…","type":"text","text":"…"}}
```

with these `type` values and payloads:

| `type` | meaningful fields |
| --- | --- |
| `step_start` | `part.type` |
| `text` | `part.text` (may contain newlines) |
| `tool_use` | `part.tool`, `part.state.status`, `part.state.input`, `part.state.output`, `part.state.title` |
| `step_finish` | `part.reason`, `part.cost`, `part.tokens.{input,output,reasoning,cache}` |
| `error` | error payload |

## Goal

Make the output panel show what each `opencode` event *means* instead of its raw
JSON, while leaving the log feed, the loop, and every existing log line
(`$ opencode run …`, `===== Iteration n/m =====`, `New best score: …`, …)
rendered exactly as they are today.

## What it should do

1. Parsing is **render-only**. `ClimberState.Logs` and `HillClimberService` are
   untouched; the loop's tests keep passing unchanged.
2. Each log line is classified as either an opencode JSON event or a plain line:
   - A plain line (anything that is not a well-formed opencode event object) is
     rendered as today, with the existing `.log-line` colour classes from
     `LineClass(...)`.
   - A line whose payload after the `[HH:mm:ss] ` stamp parses as JSON with both
     a string `type` and an object `part` is rendered structurally.
3. Structured rendering per event type drops the JSON boilerplate:
   - `text` — the text itself (newlines preserved).
   - `tool_use` — the tool name plus a one-line input summary, then the tool
     output as plain text. The duplicated `metadata.content` / `rawInput` blobs
     are not shown.
   - `step_start` — a dim marker.
   - `step_finish` — a dim marker with reason, cost and token counts.
   - `error` — shown as an error line.
   - Any unrecognised event type falls back to a plain line (never a crash,
     never a blank row).
4. The `[HH:mm:ss]` stamp is preserved for structured lines (rendered dim) so
   ordering and context are still readable.
5. Colouring keeps working: a structured line still carries the class derived
   from the same substring rules (`ERROR` → err, `Revert`/`Regression` → warn,
   `New best`/`Completed` → ok, `$ `/`=====` → cmd).
6. Parsing must not throw on malformed input (truncated JSON, a lone `{`, a
   number, an array, empty payload). Anything unparseable or unrecognised is a
   plain line.
7. Parsing is cached per line so the frequent `StateChanged` re-renders do not
   re-parse the whole (bounded, 5000-line) feed each time.

## Out of scope

- Truncation/`<details>` reveal and pretty-printing the full raw JSON — this
  feature **structurally replaces** the raw event, it does not hide it.
- Changing `HillClimberService.Log`, `MaxLogLines`, the log feed, or the
  `Iteration`/`Result`/`New best` messages.
- Filtering, search, or collapsing whole event groups.
- Parsing `dotnet test` output structurally (only `opencode --format json` is
  structured today).
- Any change to `LineClass`'s substring rules.

## How we know it is done

`dotnet test --nologo` stays green (**53 passing today**, plus new tests), and
`LogFormatterTests` asserts, for each of:

- a plain loop line keeps its text and no structured fields;
- a `text` event yields the text and kind `Text`;
- a `tool_use` event yields tool name, a non-empty input summary, and the tool
  output — and does **not** expose `metadata.content` / `rawInput`;
- `step_start` and `step_finish` yield their kinds and a non-empty summary;
- an `error` event yields kind `Error`;
- malformed JSON, a JSON array, a JSON object without `type`/`part`, and an
  unknown `type` all fall back to kind `Plain` without throwing;
- the `[HH:mm:ss]` stamp is extracted for a structured line.

Then browser-verify: a real `opencode run --format json` climb against the
throwaway repo in the Playwright harness shows readable events in `[03] output`
instead of raw JSON.

## Fixed point for review

`.scratch` spec work starts from `64f1f24` (`docs: handoff always emits a
next-session starting prompt`). `/code-review 64f1f24`.
