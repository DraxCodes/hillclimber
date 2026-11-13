# Specify `ILogger` plumbing in `HillClimberService`

Status: open
Type: task

`HillClimberService` does not take an `ILogger`, so there is no structured logging
surface for a run. Decide whether the service accepts an injected `ILogger<T>`,
how it is registered in `Program.cs`, and which events get logged versus which stay
in the bounded UI log feed.
