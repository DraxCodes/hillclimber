---
description: Install the optional engineering skill pack for this project
---

Install the optional engineering skill pack defined in
`.opencode/engineering-pack.json`. Run:

```sh
node .opencode/scripts/install-engineering-pack.mjs
```

The pack is opt-in and project-scoped; it is not part of the universal base.
After it finishes, report which skills were installed and tell the user to run
`/setup-matt-pocock-skills` once to configure their issue tracker and labels
(several pack skills depend on it).

$ARGUMENTS
