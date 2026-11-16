# Spec: session persistence & multi-session support

## Context

The sessions rail UI already exists (`MainLayout.razor:73-167`) but shows exactly one
hardcoded row — the current run — plus an honest empty state: *"Runs are not persisted
yet."* Everything in `Program.cs` is a singleton, so there is exactly one in-memory
run/climb per process and nothing on disk. This change adds a durable session store,
makes sessions resumable, and wires the rail to it.

Fixed point for review: `20d2f6e`. Baseline `dotnet test`: 501 passed, 0 failed, 0 skipped.

## Decisions (locked with the user before coding)

1. **Storage — JSON files per session.** One JSON file per session under a configurable
   directory. Atomic writes (write temp file, then rename). No new dependencies. Manual
   delete from the rail; nothing auto-pruned.
2. **Session identity — mint on first run.** The app mints a fresh session id when the
   first run/climb starts. Title = first prompt, truncated. A session row never exists
   with zero messages. The id is deliberately separate from the browser auth session in
   `ClimberAuth` (`climberAuth.sessionId` in localStorage) — that is an auth token, not
   a conversation.
3. **What persists — full transcript, resumable.** Prompt, ordered transcript entries
   (including the per-subagent tabs), status, timestamps, metrics/spend. Finished
   sessions are resumable: the next prompt continues the same conversation.
4. **Resume mechanics — app-minted id via `--session`.** The app mints its own session
   id and passes it as `--session <id>` on every `opencode run` invocation. Resume
   simply re-invokes opencode with the same id; opencode continues the conversation
   server-side (its own storage). This is deterministic and does not depend on
   capturing a sessionID from the event stream.
5. **Concurrency — one live run.** At most one engine run per process. Switching
   sessions while a run is live does not stop it: the run continues in the background,
   the rail row keeps its live status dot, and switching back re-attaches.
6. **Switching — rebind on switch.** Clicking an old session loads its transcript and
   rebinds it as the active conversation (the next prompt continues it). `+` archives
   the current session into the rail and starts a fresh one.
7. **Climb iterations — share the session.** Every `opencode` call in a session —
   including each hillclimb iteration — passes the same `--session` id, so the climber
   sees its own prior attempts. This changes today's fresh-conversation-per-iteration
   behavior; accepted.
8. **Transcript on disk — display entries.** Persist the parsed, display-truncated
   `LogEntry` records (same limits the UI applies: tool output ≤ 2000 chars, reasoning
   ≤ 4000). Raw JSON event lines never touch disk (per
   `.scratch/security-pass/issues/01-tool-output-logging.md`). Truncated tool output
   does persist — that is the accepted trade-off for history disclosures.
9. **File location — user app data.** Default under the user's app data
   (`%APPDATA%`/`ClimberWeb/sessions`), overridable via `Climber:SessionsPath`
   (follows the `Climber:*` convention). No `.gitignore` entry needed.
10. **Retention — manual delete.** A delete action per session in the rail. Nothing
    auto-pruned.

## Module design

### `ISessionStore` — the seam

The store sits behind one interface so playback/fixture mode stays in-memory and the
UI never touches the filesystem:

```csharp
public interface ISessionStore
{
    IReadOnlyList<SessionSummary> List();
    SessionRecord? Load(string sessionId);
    void Save(SessionRecord record);      // atomic write-then-rename
    void Delete(string sessionId);
}
```

- `SessionSummary` — id, title, method, status, started/finished timestamps, spend,
  estimated spend and total tokens. Everything the rail renders, and the running
  totals each turn accumulates into.
- `SessionRecord` — the summary, the persisted transcript (ordered display-`LogEntry`
  rows, the reader's prompts included as their own rows), and the folder a resume runs
  in. The record's id doubles as the opencode `--session` binding.

Two adapters make the seam real: `JsonSessionStore` (filesystem) and an in-memory
fake for tests and fixture mode. `PlaybackRunService`/`PlaybackHillClimberService`
keep working unchanged: the store is orthogonal to the run services; fixture mode
simply never has sessions persisted from playback runs (a playback host is a display,
not a runner).

### `SessionCoordinator` — lifecycle

A singleton that owns the *current session id* and mediates between the rail, the run
services, and the store:

- **On first run/climb start:** mint id, create the record in memory; persist from the
  run's first state change, so a launch the services reject leaves no row behind.
- **On state change:** update the record's status/timestamps/metrics, adding the run's
  own spend and tokens to the session's running totals, and persist.
- **On transcript growth:** append new entries and persist (debounced — the run feed
  can burst; flush at most once per second and on terminal states).
- **On switch:** persist the outgoing session's final state, load the target, publish
  its transcript, set it as the active conversation. A live run keeps running; its
  session keeps updating in the background.
- **On `+`:** flush the current session, then clear the active id (the next run mints
  a fresh one).

### Resume path

`RunOptions`/`ClimberOptions` gain the session id; `OpenCodeRun.Request` gains a
`--session <id>` argument. On resume, the coordinator rebinds: the page shows the
stored transcript, and the next `StartAsync` passes the same id, so opencode
continues the conversation. The transcript view for a resumed session shows the
persisted history followed by the new live entries.

### UI changes (`MainLayout.razor`, `Home.razor`)

- The rail renders the store's session list, most recent first: current session row
  (live status dot, real title) followed by earlier sessions (summary only: title,
  status label, relative time). Delete button on every stored row that is neither on
  screen nor live (always visible, so it works on touch, with an inline confirm; the
  row on screen and a live row have none because their records would be written
  straight back).
- `SessionTitle` comes from the coordinator: first prompt truncated, or "new session"
  before the first run (a row that has not started, rather than one that is current).
- Clicking an earlier session rebinds (decision 6). The live-run row keeps its
  pulsing dot even when viewed from another session.
- `+` archives the current session and focuses the composer for a fresh one.
- The empty state disappears once sessions exist; the copy stays for a genuinely
  empty store.

## What is explicitly not persisted

- Raw opencode JSON event lines (security rule).
- `ClimberAuth` browser-session state (already in localStorage; unrelated).
- Provider API keys, PIN, or any configuration.

## Verification / done

- `dotnet test` green with new unit tests for: store round-trip (save/load/delete,
  atomic write), summary listing order, coordinator lifecycle (mint on first run,
  rebind on switch, archive on `+`, live-run-continues), `--session` argument
  construction, transcript truncation limits on persist, and the resume path
  (same id passed on the next run).
- Playback/fixture mode verified unbroken: `CLIMBER_SEED=1` host still renders the
  fixture transcript; the store is empty and no file is written.
- Visual pass: temporary sample sessions (throwaway, uncommitted), screenshots at 900,
  375 and 1280 in light and dark, then the seed removed.
- Free harnesses (`theme-nav`, `security`, `providers`, `polish`) run against an
  unseeded host; `surfaces.mjs` **not run** (paid) and named as such.
- `/code-review` since `20d2f6e`; then one logical commit
  (`feat(sessions): …`).
- `/status` fingerprint regenerated before the final commit (`scripts/status.ps1`,
  then `scripts/status-ui.ps1` against a live host).

## Out of scope

- Parallel runs per session (one live run per process, per decision 5).
- Auto-pruning or retention policies (decision 10).
- Branching/forking sessions.
- Editing or re-titling past sessions beyond the first-prompt rule.
- Any database.
