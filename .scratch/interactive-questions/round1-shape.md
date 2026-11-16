# Round 1 — shape seat

**Proposal.** A question is a transcript row, not a new surface. Pending, it is one
`Trace` summary line carrying the question text; its body is a group of
`aria-pressed` buttons, the option idiom the method, output and platform switchers
already keep. Answered, it folds to one line naming the choice. No new row
shape, no card in a panel, no Dialog, RadioGroup or Field.

It wins: the parts exist and pass R1–R7; pending and answered are one component;
the keyboard contract is one three shipped controls keep.

**Seam.** Not `LogFormatter`, not `Home.razor`. It is the boundary that yields raw
event lines and would accept a reply: one narrow channel — emit, submit, cancel —
with a CLI adapter today, a server adapter later. Downstream stays put; the UI is
scoreable against a seeded channel before transport settles.

**Defers:** free text, several questions per turn, permission prompts, and a
restored pending question (history shows it read-only, `unanswered`).

**Cost:** two `LogKind`s, one fold, one seam. **Top risk:** a seam too thin — a
reply needing an id the CLI adapter cannot mint leaks into the component, and both
adapters move together. **Changes my mind:** proof v2.3.0 emits the question and
accepts an answer on the CLI stream; the server adapter disappears rather than
waits.

**Positions**

1. *Fidelity vs scope* — smallest answerable surface: question, options, one action.
2. *One click vs staged* — one click. A confirm buys misclick margin at a third
   state to render and pin.
3. *Free text vs options* — options only. Free text concentrates injection, secrets
   and persistence; an unlisted answer is a composer message, not a field.
4. *Transport* — `opencode serve`, narrowly, behind the seam. Rejected: stdin
   (undocumented — a fabricated protocol that fails silently on upgrade); keeping
   `run` (that is the deferral, not an answer). Costs: a second process to own and
   orphan-clean, a loopback HTTP surface behind the PIN gate, and `CommandLine`'s
   argv line no longer describing the run.
5. *New surface vs rows* — existing rows. `Questionnaire` is the element name;
   compose it from `Trace` plus a button group.
6. *Answered* — fold to a line naming the choice; only untaken options are lost, and
   dead forms are worse.

**Cut: the port.** No RadioGroup, Field or Questionnaire file: a third option
pattern, for a row that appears once a run, is drift with a registry citation.