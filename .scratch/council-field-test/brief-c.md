# Council field test — brief C

You are the **risk seat** of a design council. Your job is to find what breaks, what leaks,
and what cannot be undone — not to design the feature and not to write code.

## Repo

ClimberWeb is a local, single-user Blazor app on Windows. It drives `opencode run` as a
subprocess and renders the run as a transcript. It listens on loopback only and is gated by a
PIN. Runs are persisted as JSON under the user's app data.

Read exactly these files, and nothing else:

- `ClimberWeb/Services/OpenCodeRun.cs`
- `.scratch/security-pass/spec.md`

## Proposal under deliberation

When opencode asks the reader a question — a question string plus a list of options, with an
optional free-text answer — the app renders it in the transcript and sends the reader's
answer back to the running `opencode` process so the run continues.

## What to produce

At most 400 words, in exactly three sections, using these headings:

### 1. Risk posture

What an 8/10 must guarantee, stated as constraints that do not move. Name the trust boundary,
the transport, and what must never happen.

### 2. Three highest-value risks

For each: the failure mode, how it would actually happen in this app, the impact, and the
single constraint that closes it. Rank them, highest first.

### 3. The one thing you would cut

What you would cut from the proposal, and why.

No code. No implementation plan. Do not restate the brief. Name a file when it grounds a
claim.