# Usability rubric (`usability`)

Use when the artifact is human-facing but not a screen: an API a developer calls,
a CLI, error messages, log output, a config file, documentation, an event or a
notification. The question is whether a competent first-time user gets value
without guessing.

The floor, the anti-inflation rules and the stop condition are the same as every
hillclimb run: the seam's `checks` pass, scores are whole numbers, and 8 ships
unless a 10 was demanded.

## How to look

Each probe is an action, not an impression. Record the exact commands, request
bodies, responses and strings.

1. **First-use test.** With no documentation, attempt the primary task. Count
   every lookup, guess and retry. Each is a friction flaw.
2. **Error recovery.** Trigger the three most likely errors (missing required
   field, wrong type/format, not found or forbidden). Every error must name what
   happened, the offending input, and the next action.
3. **Name read.** Read every public name aloud: endpoint paths, flags, fields,
   enum values, env vars, log keys. Flag synonyms for one concept, abbreviations
   a newcomer cannot expand, and names that do not match their effect.
4. **Shape check.** The happy-path call is as short as the task allows. Required
   fields are only those that cannot be defaulted; optional ones have sane
   defaults.
5. **Output read.** Read one success response and one failure response as a
   stranger. Can they tell what to do next without the source? Is anything
   internal (stack trace, ORM error, SQL, internal hostname) exposed?
6. **Scale of the surface.** For a CLI: no args, `--help`, bad args, piped input,
   long output. For an API: empty, one item, a large page, a malformed request.
   For docs: a cold read of the first screen and the "I want to do X" path.
7. **Consistency sweep.** The same concept uses the same word, casing and shape
   everywhere. Two spellings of one parameter is a flaw.

## Levels

### 10 — Obvious

- A first-time user completes the primary task with no lookup and no retry.
- Every error names the cause, the input and the next action; no internal detail
  leaks; recovery is one step.
- Public names are self-explanatory and used consistently; defaults let the
  common case omit every optional field.
- Every probe passes and the reviewer can say "no flaw found" without hedging.

### 9 — Finished, with one named edge

- The main path and the common errors meet the level-10 traits.
- Exactly one named, low-exposure imperfection remains: a rare error path, an
  advanced option, or one inconsistent label a first-time user would not meet.
- If the reviewer cannot name the edge, score 10; if the edge is on the main path,
  it is 7 or lower.

### 8 — Clean and clear

- The primary task succeeds without documentation; at most one pause that
  resolves itself.
- Success and failure outputs are readable, and every failure offers a next step.
- No placeholder, `TODO`, debug dump or internal stack trace in a user-facing
  message.
- Names are consistent and match their effect.
- Remaining flaws are nits a user would not name.

### 7 — Nearly clean

- The main path works but one named rough edge remains on it: a confusing flag, a
  missing default, or an error that needs a second read.
- Everything else meets the level-8 traits.

### 6 — Usable with visible seams

- The task is completed without instructions, but there is repeated friction:
  inconsistent naming, a required field that should default, an error that omits
  the next action, or a docs step that contradicts the CLI.
- A user notices the seams but is not blocked.

### 5 — Usable but clumsy

- Every function works, but the surface makes the user do work it should have
  done: jargon, extra steps, a required field that is guessable, feedback that
  does not say what changed.
- At least one common error is unhelpful.
- A user can get value after mental effort.

### 4 — Works after guessing

- The primary task succeeds only after a guess or a retry.
- A public name does not match its effect, or a common error is missing or
  misleading.
- At least one required input has no documented shape and no example.

### 3 — Looks unfinished

- Placeholder or contradictory output appears on a normal path.
- Several errors expose internals or omit recovery.
- A first-time user needs the source or a colleague before they can start.

### 2 — Mostly broken

- A primary path cannot be completed from the public surface alone.
- Outputs contradict the documented shape, or a failure is silent.
- Only someone who already knows the implementation can use it.

### 1 — Unusable

- The primary task cannot be completed at all.
- The surface is undocumented, unnamed or self-contradictory.

## Evidence a score must cite

A score without this evidence is treated as below 8.

- **Attempt transcript:** the exact commands or requests tried without docs, and
  where the user paused or guessed.
- **Strings:** the exact success and failure messages, quoted, with the input that
  produced them.
- **Names:** the list of public names reviewed, and any synonym or mismatch found.
- **Flaws:** each states what, where, and the condition that exposes it.
- **Floor:** the exact commands run and their observed result; anything not run is
  labelled "not run".

Scorecard shape:

```text
Rubric: usability
Score: 7/10 — Nearly clean
Target: 8
Evidence: transcript <path or inline>; strings <quoted>; names <list>
Flaws holding it at 7:
  1. <what> — <where> — <condition>
Why not 8: <the level-8 trait that is missing>
Why not 6: <the level-6 trait that is absent>
Floor: <command> <result>; <not run>
```

## Anti-inflation rules

- An 8 or above must name the level-7 flaws that are demonstrably absent.
- A 10 must state that no flaw was found across every probe.
- "Ergonomic" without a transcript of the first-use attempt is void.
- Do not credit an API or CLI whose errors were never triggered.
- A doc that describes the happy path only cannot score above 6.