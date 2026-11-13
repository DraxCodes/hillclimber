# Personal path committed as the default folder

Status: open
Type: task

`appsettings.json:12` hard-codes `C:\Users\joelp\code`; `Home.razor:425` repeats it as a
fallback. `AGENTS.md:50-51` says never commit personal data.

## Fix

Drop `DefaultFolder` from committed config (empty/placeholder), and fall back to the user
profile or an empty field.
