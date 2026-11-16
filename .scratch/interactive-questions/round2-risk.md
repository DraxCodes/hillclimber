# Round 2 — Risk seat (cross-critique)

## 1. Where the majority is wrong

"Verify `opencode serve` first" survives as a principle but the bar everyone states —
"event emits, reply accepted" — is a happy-path test. It doesn't verify the thing that
actually gates shipping: does serve bind loopback by default, does it carry its own auth
that conflicts with this app's PIN, and does a reply race-fail safely on disconnect?
"Verified" risks becoming a green light for transport before anyone checks the bind
address or auth model. Tighten the agreement: verified-and-safe-by-default, not merely
verified-functional, or the gate is cosmetic.

## 2. One click vs staged — the case I didn't take

Chair/shape's best case: options are vetted, listed, non-destructive text; a confirm step
taxes the common case to guard a rare misclick, and the transcript already makes every
answer auditable after the fact. It doesn't move me — "no edit" (a fixed non-goal) means
auditable-after isn't recoverable-after. A wrong click still executes against a live run
with no undo. Irreversibility, not frequency, is the structural argument; staged confirm stands.

## 3. Free text — the chair's case

Refusing free text excludes the one real question with no listed answer, making the
feature a toy for the common case and useless for the hard one. It doesn't move me:
shape and experience's "escape to the composer, one turn down" already answers that case
through an already-trusted channel, without opening a second unescaped field with the
same injection/secret/persistence exposure the brief names as the concentration point.

## 4. Top failure mode and the non-negotiable constraint

Hardest self-critique: my round-1 claim that "options-only is provably closed" is
incomplete. The chair's risk is sharper — a question whose *options themselves* are
consent actions ("Allow access to `C:\Users\…`?" / Yes/No) turns an options-only surface
into an unreviewed permission grant, with no free text involved at all. That's the top
failure mode: the line between "question" and "permission" blurring unnoticed.
Non-negotiable: every answer, option or text, is data sent to the model — never an app
action. No UI path may execute a filesystem/tool/permission effect from an answer click.
