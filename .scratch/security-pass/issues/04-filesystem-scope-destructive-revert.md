# Unbounded filesystem browse, arbitrary working directory, destructive revert

Status: open
Type: task

`FileBrowser.cs:38-109` lists every drive and folder; `ProcessCommandRunner.cs:22` runs in
any user-supplied `WorkingDirectory`; `HillClimberService.cs:293` runs `git checkout -- .` in
the chosen repo. This is by design for a folder picker, but combined with a weak PIN a LAN
attacker who unlocks can point a shell-capable agent at any directory (e.g. `C:\Users\joelp`)
and discard uncommitted work. Preflight only warns when the target is not a git repo
(`ClimbPreflight.cs:47-50`).

## Fix

Constrain browsing/execution to a configured workspace root (allowlist), and require explicit
confirmation before a destructive revert.
