---
name: grilling
description: Grill the user relentlessly about a plan, decision, or idea. Use when the user wants to stress-test their thinking, or uses any 'grill' trigger phrases.
---

Interview the user relentlessly until you reach a shared understanding. Map this as a **design tree**: every decision branches into the decisions that hang off it.

Work the tree in **rounds**. The **frontier** is every decision whose prerequisites are already settled: the questions you can ask _now_ without guessing at answers you haven't heard yet. Ask the whole frontier in one round, then wait for the user's answers before the next round.

## Ask through the question panel

When the **`question` tool** is available, the round _is_ the panel: one call carries the whole frontier, so the user answers it in place instead of reading a wall of prose and retyping answers. Map each question onto its fields:

- `header` — the short title, numbered and at most 30 characters: `Q1 — Stack`.
- `question` — the body: the decision, the context it hangs on, and what each answer implies. Multi-paragraph is fine.
- `options` — the two to four answers you would actually accept, the first being your recommendation with `(Recommended)` at the end of its label, e.g. `Postgres (Recommended)`. The panel appends its own free-form answer, so don't offer "something else".
- `multiple: true` only when the answers can genuinely combine; otherwise leave it off so a single choice stays explicit.

Word the recommended option so picking it is the easy path: the user should be able to accept your recommendation without retyping it. A question with no enumerable answers still goes in the panel — offer the two or three live positions and let the free-form field carry the rest. Don't restate the round as chat prose, before or after the panel.

Only when the `question` tool is unavailable in this client, fall back to prose: number each question in the round, put your recommended answer beneath it, and separate the questions with a rule. Word each question so "yes" accepts your recommended answer.

```
❓ **Q1** - **<question title>**: <question body, might be multiple paragraphs, including multiple choices>
➡️ <your recommended answer>

---

❓ **Q2** - **<question title>**: <question body, might be multiple paragraphs, including multiple choices>

➡️ <your recommended answer>
```

Each round the user answers reshapes the tree: settled decisions push the frontier outward and unblock questions that depended on them. Recompute the frontier and ask the next round. A question whose answer depends on another question still open in this round belongs to a _later_ round, not this one.

Finding _facts_ is your job, never the user's. When a frontier question needs a fact from the environment (filesystem, tools, etc.), dispatch a sub-agent with a fresh context window to find it; don't ask the user for anything you could look up yourself. Don't block on it: a running exploration is an unsettled prerequisite, so only the questions downstream of it wait for the sub-agent to report; ask the rest of the frontier now. The _decisions_ are the user's: put each to them and wait.

The session is done when the frontier is empty: every branch of the design tree visited, nothing left silently assumed. Do not act on it until the user confirms you have reached a shared understanding.
