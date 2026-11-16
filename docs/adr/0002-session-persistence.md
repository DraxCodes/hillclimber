# ADR 0002: Session persistence

- **Status:** accepted
- **Date:** 2026-11-16
- **Context:** sessions rail (`MainLayout.razor`), `ClimberWeb/Services/SessionStore.cs`, `ClimberWeb/Services/SessionCoordinator.cs`

## Context

The rail had one row and an honest apology: "Runs are not persisted yet." Every service in
`Program.cs` is a singleton, so the process holds exactly one live run at a time and nothing
survives a restart. The page could show the current transcript, but a reader who came back
later had no history and no way to continue a conversation opencode still remembered.

Persistence has to answer four questions at once: *where* the transcript lives, *what* an
opencode conversation is keyed by, *how much* of the transcript is worth keeping, and *what*
happens when a session is viewed while another run is live.

## Decision

**Store — one JSON file per session, no new dependencies.** `JsonSessionStore` writes
`%APPDATA%/ClimberWeb/sessions/<id>.json` (overridable with `Climber:SessionsPath`), one
file per session, each write to a temp file and then `File.Move(overwrite: true)`, so a
reader never sees a half-written or missing session. Reads are tolerant: a corrupt or
hand-edited file degrades to a missing session instead of taking the rail down. Deletion is
manual, from the rail; nothing is auto-pruned.

**Identity — the app mints the id and passes it as `opencode --session <id>`.** A fresh
session id is `ses_<guid>` and never changes. opencode continues the conversation
server-side and creates it when absent (verified against opencode 2.3.0), so resume is
deterministic and needs no capture of ids from the event stream. The id is deliberately
separate from `ClimberAuth`'s browser session id: that one is an unlock token, not a
conversation. A row is titled by its first prompt (first line, 48 characters); a session
with zero messages never exists in the store.

**What persists — the display transcript, not the event stream.** The coordinator persists
`LogEntry`-shaped rows: kind, timestamp, text, tool fields, status. The raw opencode JSON
line is dropped at the seam (`StoredEntry.From`), per
`.scratch/security-pass/issues/01-tool-output-logging.md`; a restored row rebuilds a
synthetic render key from its position. Tool output is additionally capped at 2000
characters on disk — the live view keeps the whole result, but a file read or a command can
otherwise put megabytes in one session file. Prompts are stored as their own rows
(`LogKind.UserPrompt`), so history reads as a conversation rather than a wall of answers.

**Concurrency — one live run, history is a view.** `SessionCoordinator` owns the active id,
the live id and the transcript. Switching sessions (or archiving with `+`) never stops a
live run: the coordinator keeps appending it to its own record, the rail keeps its live
dot, and switching back re-attaches to the freshest in-memory record rather than the
debounced store. A session that is live or on screen cannot be deleted; a live record would
write itself straight back. Switch and archive flush the live record first, so a restart
right after either cannot lose the last second of its feed.

**Totals belong to the session, not the turn.** A run reports only its own spend and
tokens, so the coordinator adds each run's numbers to the session's running totals before
writing: a resumed conversation shows what the whole session cost, matching its
`this session` label. Per-run figures stay where they are — on the live telemetry band.

**Write cadence — debounced, terminal flush.** A run re-raises its whole feed on every line,
so the coordinator formats only the new tail and saves at most once a second while live, and
always on `Completed`/`Stopped`/`Failed`. The listing is cached and invalidated on write, so
rendering the rail does not re-read every file. A stored `Running` row with no live binding
in this process is reported as `stopped`: a run cannot outlive the process, so that row is
what a crash left behind, and it should not pulse for work that is not happening.

**Launch is bound before it starts.** `Launch` calls `Begin` (mint id, append the prompt
row, make it active) and then starts the service with that id; a synchronous refusal calls
`Abandon`, which restores the previous view and leaves no row. The pending bind is promoted
to "live" by the run's own first state change, so a background run is never displaced by a
launch that was rejected.

**Fixture mode stays a display.** With `CLIMBER_SEED`, `Program.cs` registers
`MemorySessionStore`, the playback services carry no session id, and no file is written.

## Consequences

- A resumed session continues opencode's conversation (context is opencode's), while the
  page shows the app's own truncated transcript. The two are related but not identical: the
  stored transcript is what the rail and the page render, not a re-import of engine state.
- Hillclimb iterations now share one conversation per session, so the climber sees its
  earlier attempts. That is a deliberate change from the previous fresh-conversation-per-
  iteration behaviour.
- Transcript history loses the raw payload of unrecognised events and the tail of large
  tool outputs. Both are accepted: history is for reading, not for replaying events.
- No pruning means the folder grows with use; deleting from the rail is the only cleanup.
- One run per process is unchanged. Two tabs that both try to launch resolve the race in
  the coordinator (the second gets a clear "a run is already live" error) rather than
  corrupting a transcript.

## Alternatives rejected

- **A database (SQLite/EF):** a dependency and a schema for a single-user local app whose
  records are small; JSON files are inspectable and enough.
- **Capture `sessionID` from the event stream:** order- and version-dependent, and useless
  before the first event; minting the id up front is deterministic.
- **Persist raw event lines:** re-renders would be exact, but it would put full opencode
  payloads (and their tool output) on disk, which the security pass rejected.
- **Stop the live run on switch:** simpler state, but it throws away work the reader never
  asked to cancel.
- **Auto-prune by age or count:** deletes a conversation without a person deciding to. The
  rail's delete action makes the decision explicit.