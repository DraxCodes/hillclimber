# No PIN attempt throttling or lockout

Status: open
Type: task

`ClimberAuth.TryUnlock` (`ClimberAuth.cs:69-82`) has no rate limit, lockout, or delay. A
4-digit PIN is 10,000 guesses; anyone on the LAN can unlock and then drive `opencode` (which
has `shell: allow` in `opencode.json:14`) with an arbitrary prompt in an arbitrary folder.
OWASP Authentication Cheat Sheet and ASVS V6.1.1/V6.3.1 require anti-automation controls.

Rate limiting beyond the PIN was explicitly out of scope for the security pass (decision 2),
so this is tracked rather than fixed. The pass does remove the shipped default PIN from the
LAN path, which removes the trivial-guess case.

## Fix

Add per-session/IP attempt throttling with exponential backoff or a lockout window.
