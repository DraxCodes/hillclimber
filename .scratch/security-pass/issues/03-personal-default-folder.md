# Personal path committed as the default folder

Status: resolved
Type: task

`appsettings.json:12` hard-coded `C:\Users\joelp\code`; `Home.razor:425` repeated it as a
fallback. `AGENTS.md:50-51` says never commit personal data.

## Fix

Dropped `DefaultFolder` from committed config; `Home.razor` now falls back to the user
profile, then the current directory. Fixed inline during the security pass after review
flagged it as a hard standards breach in a file the pass already touched.
