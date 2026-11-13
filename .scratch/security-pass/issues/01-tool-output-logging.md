# Tool output and prompts are logged verbatim at `Information`

Status: open
Type: task

`RunService.cs:241` and `HillClimberService.cs:348` write every `opencode` JSON line and
every `dotnet test` line to `ILogger` in the clear. That stream can contain source code,
file contents the model read, and potentially secrets from the target repo. Microsoft's
Blazor SSR threat-mitigation guidance warns against logging sensitive data.

## Fix

Log at `Debug`, or log only event types/counts and redact payloads. Never persist raw tool
output.
