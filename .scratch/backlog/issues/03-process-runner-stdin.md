# `ProcessCommandRunner` closes stdin by redirecting it

Status: open
Type: task

`ProcessCommandRunner` sets `RedirectStandardInput`, which closes the child's
stdin immediately. That is fine for the current commands (`opencode run`,
`dotnet test`, `git`) but is a silent trap for any future command that expects to
read from stdin.

Decide whether to leave stdin connected, feed it, or document the constraint.
