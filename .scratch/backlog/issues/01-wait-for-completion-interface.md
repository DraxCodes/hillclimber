# Track `WaitForCompletionAsync` on `IHillClimberService`

Status: open
Type: task

`WaitForCompletionAsync()` is public on the concrete `HillClimberService` but is not
on the `IHillClimberService` interface, so callers that only see the interface
cannot await a run's completion.

Decide whether it belongs on the interface (and make it explicit there) or should
stay concrete and be reached another way.
