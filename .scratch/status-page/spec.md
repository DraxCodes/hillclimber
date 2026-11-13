# Spec: Project status page

## Context

The dashboard is a single page (`/`) that drives a hill-climb run. There is no view
of the project's own health and history — how often we iterate, what changed and
why, whether the suite still passes, what coverage looks like, what we have
tracked but not started, and which model/workflow produced the work.

This spec adds a **status page** at `/status`, styled with the existing design
tokens and `.panel` chrome, that answers those questions for *this* repository.

Following the session-start decisions (see `## Decisions`), the page uses a
**hybrid** data source: git history is read **live** at request time (cheap and
always current), while the slow, machine-coupled parts (coverage / test status)
come from a **committed, pre-generated artifact**. Nothing expensive runs on page
load and the page never throws when git or an artifact is missing.

## Decisions (locked with the user before specing)

1. **Data source — hybrid.** Live `git` for changelog, cadence, and repo state;
   a committed `status/coverage.json` artifact for coverage and test status.
2. **Simplified per-change description — trailer/sidecar going forward.** New
   commits carry a `Description:` git trailer; a committed sidecar
   (`status/changelog.json`) backfills existing history. Resolution order per
   commit: trailer → sidecar → derived from the subject.
3. **Model per change — base + workflow, honest.** Git records no model, so the
   page shows the base model (from `opencode.json`) and describes the workflow
   once, and labels per-change model as the base default rather than inventing it.
4. **Cadence axis — per day, auto-fallback to hourly.** Bucket commits by
   calendar day; when the history spans under ~2 days, bucket by hour instead so
   the graph stays meaningful.
5. **Coverage — script to committed artifact.** A script runs
   `dotnet test --collect` and writes `status/coverage.json`; the page reads it,
   or shows a "not measured yet" state.
6. **No `/hillclimb`.** This is a feature, not a numeric objective.

## Goal

Render a single `/status` page showing, without throwing and without running an
expensive process on load: repo state, a changelog with one-line descriptions,
coverage + test status, an iteration-cadence graph, tracked-but-unstarted issues,
and the models + workflow (base and per-change).

## What it should do

1. **Route and nav.** `/status` is a new interactive-server page; the layout
   gains a link to it from the brand/`bar-actions` area. `/` is unchanged.
2. **Repo state.** The page resolves the repository root (config
   `Climber:RepoRoot`, else walk up from the app base directory to the folder
   holding `.git` / the `.slnx`) and shows branch, short HEAD, and whether the
   worktree is dirty.
3. **Changelog.** The last N commits (N bounded, e.g. 200) are read live with
   `git log`. Each row shows the short hash, the date, and a **simplified
   one-line description** resolved as: a `Description:` trailer on the commit, else
   the sidecar entry in `status/changelog.json`, else the subject with its
   conventional-commit prefix (`type(scope): `) stripped.
4. **Coverage + tests.** `status/coverage.json` (if present) yields passed/failed/
   skipped counts and a line-coverage rate; the page renders pass/fail status and
   a coverage bar. If the artifact is absent, the section shows a clear
   "not measured yet" state and the command to generate it.
5. **Cadence.** Commits are bucketed by day, or by hour when the span is under
   ~2 days, and rendered as an inline SVG bar chart (no chart library, no JS).
6. **Tracked issues.** Markdown under `.scratch/**/issues/*.md` whose `Status:` is
   not resolved/closed is listed with its feature, number, title, and status.
   An empty result renders an empty-state line, never an error.
7. **Models + workflow.** The base model (read from `opencode.json`) and the
   workflow are shown: the everyday loop (spec → implement → `/code-review`) and
   the hillclimb trio (`task-orchestrator`, `plan-architect`, `quality-gatekeeper`,
   read from `.opencode/agents/*.md`). Per-change model is labelled honestly as the
   base default, with a one-line note that git does not record it.
8. **Resilience.** Not a git repository, a missing git binary, a shallow clone, an
   unreadable artifact, and a missing `.scratch/` all degrade to a populated page
   with a warning, never a thrown exception.
9. **Testable parsing.** All parsing/derivation (git log, trailer/sidecar/subject
   description, cadence bucketing, coverage artifact, issue scan, agent
   front-matter) is pure and unit-tested without spawning processes or reading the
   real repo.

## Out of scope

- Running tests or coverage live; the page never invokes the test suite.
- Backfilling a `Description:` trailer into existing commit objects.
- Auth changes: `/status` is read-only and needs no PIN (the climb controls on
  `/` stay gated).
- Persisting climb-run history (runs are not persisted today); the cadence graph
  is derived from git, not the loop.
- Any charting dependency or client-side JS framework.

## How we know it is done

- `dotnet test --nologo` stays green (77 passing today) plus new unit tests for:
  trailer-then-sidecar-then-subject description resolution; git-log parsing;
  cadence day vs. hourly fallback; coverage artifact parsing (present and absent);
  issue scanning (`Status:` filtering, empty scan); and agent front-matter parsing.
- `dotnet run --project ClimberWeb`, then a Playwright script opens `/status`,
  asserts the sections render and there are no console errors, and screenshots it
  (light + dark).

## Fixed point for review

Session start HEAD is `e23f7e3` (`docs: record standing working preferences for
agents and handoffs`). `/code-review e23f7e3`.
