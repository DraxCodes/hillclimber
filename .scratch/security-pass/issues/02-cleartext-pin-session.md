# PIN and session id travel in cleartext over the LAN

Status: open
Type: task

`Program.cs:63` deliberately skips HTTPS redirect, and the app binds `0.0.0.0:5179`. The PIN
and the `hillclimber-session` id (`App.razor:103-120`) are therefore sniffable by anyone on
the same network. The session id is a bearer token stored in `localStorage` and trusted by
the server (`ClimberAuth`), so capture means access until the idle timeout.

TLS is out of scope for the security pass (decision 2), so this is tracked rather than fixed.

## Fix

Serve over HTTPS, or restrict to localhost/trusted network. At minimum document the risk and
shorten the idle timeout.
