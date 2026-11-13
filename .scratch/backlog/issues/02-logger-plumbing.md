# Specify `ILogger` plumbing in `HillClimberService`

Status: resolved
Type: task

`HillClimberService` does not take an `ILogger`, so there is no structured logging
surface for a run. Decide whether the service accepts an injected `ILogger<T>`,
how it is registered in `Program.cs`, and which events get logged versus which stay
in the bounded UI log feed.

## Comments

Resolved: `HillClimberService` now takes `ILogger<HillClimberService>?` and DI supplies it;
every logged line is also written through the logger. This issue was stale — the plumbing
was already in place when the status page listed it.
