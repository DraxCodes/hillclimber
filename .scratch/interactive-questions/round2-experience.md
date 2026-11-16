# Experience seat — round 2 (cross-critique)

## 1. The agreements

`opencode serve` verified-first survives: every seat's "changes my mind" clause keys on
verification, and stdin-holding is the only alternative, which the chair's deadlock
argument closes. Composing from rows survives too — I own part of it. **Where the majority
is wrong is the fold.** Four seats folded the answered state to one quiet line on taste, but
non-goals forbid editing an answer; that makes the fold *lossy with no recovery path*: the
reader who scrolls back to audit *why* the run diverged sees only `answered: option B`. An
8/10 transcript is auditable, not merely tidy. Fold, yes — but the folded line must expand
on demand (a disclosure, the idiom rows already keep) showing the question and all options
with the taken one marked. Not a persisted dead form — a collapsed record, like the output
panel. The majority conflated "no dead forms" with "no record".

## 2. One click vs staged — the other side

Staged's strongest case, stated properly: this app's one irreversible-by-design action
(sending a turn) already demands deliberation, and an answer is *more* consequential than a
composer message — it is consumed mid-run and cannot be edited. Risk seat's slip-cost
argument is real. It does not move me, because the counter is arithmetic: a question is
one-answer-once with visible options; a confirm taxes *every* question to insure a
misclick the fold-plus-audit trail already documents. Keyboard-only flow (default focus,
Enter) makes mis-clicks rare. **No change.**

## 3. Free text — the chair's case

The chair's strongest point: options-only makes the feature fail *silently* on exactly the
question that has no listed answer — the reader must know the composer trick exists. That
is a discoverability flaw, not a security win. It moves me *partway*: keep options-only at
the reply channel, but the cancelled/no-fit state must name the escape explicitly
("none fit? answer via the composer"), so the path is visible, not folklore. Not
first-class; not hidden.

## 4. What earns an 8

- Seed file (throwaway `CLIMBER_SEED` or marked seed) reaching pending, answered, and
  more-options-than-fit states — no live opencode run needed.
- Screenshots at 900 and 375, light **and** dark, each state: twelve images minimum.
- Read-through: first-run keyboard path (question appears → focus lands stated → Enter →
  fold → focus returns to composer, under ten seconds, no trap); a restored-history
  question renders closed, never live.
- Floor green: `dotnet test`, `theme-nav`, `security`, `providers`.
- One probe: at a glance, the pending question is the brightest thing below the status
  band in both themes.
