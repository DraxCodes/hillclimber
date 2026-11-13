# Spec: Full security pass

## Context

The dashboard is a single-user, local-only ASP.NET Core / Blazor Server app that binds
`http://0.0.0.0:5179` on the LAN behind a 4-digit PIN and drives the `opencode` CLI in a
user-chosen folder. A full pass was requested against the `security-auditor` remit: tests,
secrets/exposure, a general security pass, architecture vs conventions, and current best
practice looked up online.

The independent `security-auditor` run (13 Nov 2026) returned **CONCERNS**. The classic
injection/XSS/deserialisation axes are clean; the concerns are exposure: a shipped default
PIN with no throttling on an all-interfaces plaintext bind, and two unauthenticated pages
that leak repo and spend data.

## Decisions (locked with the user before specing)

1. **Scope & deliverable.** Whole project, not a diff range. One spec here; one issue file
   per finding under `.scratch/security-pass/issues/`. Fix **high-severity** findings inline
   in this pass; leave medium/low as tracked issues.
2. **Threat model.** In scope: default PIN `1234`, unauthenticated `/status` + `/activity`,
   arbitrary filesystem browse, arbitrary-folder command execution, secret handling/logging,
   hard-coded personal path, and the `0.0.0.0` bind. Out of scope: multi-user auth/accounts,
   TLS/cert management, OS-level sandboxing, rate limiting beyond the PIN, and hardening for
   internet exposure. Treat the LAN as semi-trusted; harden the local tool, don't build a
   multi-tenant product.
3. **LAN binding & PIN posture.** Keep `0.0.0.0` for phone access, but **fail closed** when
   the PIN is the shipped default: bind loopback-only and warn. A real PIN in
   `appsettings.local.json` / env is required to expose the app on the LAN.
4. **How findings are produced.** Run the `security-auditor` subagent for an independent
   list, then verify/triage manually and record the online sources checked.
5. **Done criteria.** No high-severity findings remain; every inline fix has a test;
   `dotnet test` green; the token-spending `surfaces.mjs` harness labelled **not run** unless
   we choose to spend.
6. **No `/hillclimb`.** This is a security pass, not a measurable optimisation.

## Findings (auditor, verified against the code)

| # | Severity | Finding | Disposition |
|---|----------|---------|-------------|
| 1 | HIGH | Default PIN `1234`, no throttling, bound `0.0.0.0` over plaintext HTTP (`Program.cs:41,50,63`; `appsettings.json:10`; `ClimberAuth.cs:69-82`) | **Fix inline**: remove shipped default from config; loopback-only while the default PIN is in use. Throttling filed (out of scope per decision 2). |
| 2 | HIGH | `/status` and `/activity` unauthenticated; leak git history, paths, issues, key label, spend (`Status.razor:1-3`; `ActivityPage.razor:1-5`) | **Fix inline**: gate both behind the same PIN session. |
| 3 | MEDIUM | Tool output and prompts logged verbatim at `Information` (`RunService.cs:241`; `HillClimberService.cs:348`) | File issue. |
| 4 | MEDIUM | PIN and session id travel in cleartext over the LAN (`Program.cs:63`; `App.razor:103-120`) | File issue (TLS out of scope per decision 2). |
| 5 | LOW | Personal path `C:\Users\joelp\code` committed (`appsettings.json:12`; `Home.razor:425`) | File issue. |
| 6 | LOW | Unbounded filesystem browse + arbitrary working directory + destructive `git checkout -- .` (`FileBrowser.cs:38-109`; `ProcessCommandRunner.cs:22`; `HillClimberService.cs:293`) | File issue. |
| 7 | LOW | Configurable OpenRouter base URL can exfiltrate the key (`OpenRouter.cs:36-41`; `OpenRouterClient.cs:167-171`) | File issue. |

Sound (checked, no action): no shell injection (`ArgumentList`, `UseShellExecute=false`); no
XSS sinks (no `MarkupString`/`Html.Raw`); no CORS policy (same-origin default); defensive JSON
parsing; minimal, current dependencies; no real secret in the working tree or git history;
`appsettings.local.json` gitignored and untracked.

## What it should do

### A. Fail closed on the default PIN (Finding 1)

1. `appsettings.json` no longer ships `Climber:Pin`.
2. A pure `ClimberBinding.Resolve(configuredUrls, configuredPin)` decides the listen URL:
   - a missing/empty PIN resolves to the legacy default `1234`;
   - if the resolved PIN is the default **and** any configured URL host is not loopback, the
     app listens on `http://localhost:5179` instead and reports `DowngradedToLoopback`;
   - otherwise the configured URL (or the `0.0.0.0:5179` default) is kept.
3. `Program.cs` uses the resolved URL and logs a warning when it downgrades.
4. `ClimberAuth` uses the same PIN resolution, so the gate and the bind agree.

### B. Gate every page (Finding 2)

5. A shared `AuthGate` component owns the session-id lookup, the PIN form, and the locked
   state. It renders its child content only when the session is unlocked, and invokes an
   `OnUnlocked` callback so the page loads its data only after the gate opens.
6. `/status` and `/activity` wrap their content in `AuthGate`; their data loads move from
   `OnInitializedAsync` to the gate's `OnUnlocked` callback, so nothing is fetched or
   rendered before unlock.
7. `/` keeps its existing gate; the shared session id means unlocking once unlocks all pages.

## Out of scope

- Multi-user accounts, roles, or per-user sessions.
- TLS/certificate management and HTTPS redirect.
- OS-level sandboxing of `opencode` or the target folder.
- Rate limiting/lockout beyond the PIN (filed as an issue).
- Internet-facing hardening.

## How we know it is done

- `dotnet test --nologo` green, with new tests for `ClimberBinding.Resolve` /
  `ResolvePin` / `IsLoopbackOnly`.
- The free `node scripts/ui/theme-nav.mjs` still passes; the token-spending
  `node scripts/ui/surfaces.mjs` is **not run** unless we choose to spend.
- Visual check of the locked `/status` and `/activity` panels with the app running.
- Findings 3–7 (and PIN throttling) exist as tracked issues under
  `.scratch/security-pass/issues/`.

## Evidence

- **Tests:** `dotnet test --nologo` → **185 passing** (was 165), 0 failed, 0 skipped. New
  `ClimberBindingTests` cover the default-PIN fallback, the loopback downgrade, a real PIN
  keeping the LAN bind, loopback-only lists, unparseable URLs failing closed, and host
  classification.
- **Coverage:** `powershell -File scripts/status.ps1` → 185/185, line 63.18%
  (`status/coverage.json` regenerated).
- **Bind:** starting with the shipped config logs
  `Climber:Pin is the default; listening on http://localhost:5179 only. Set a real PIN to
  expose the dashboard on the LAN.` and listens on `localhost` only.
- **Browser (free):** `node scripts/ui/theme-nav.mjs` → 3 pages, 0 failures, 0 console
  errors. A temporary Playwright check (deleted after use) confirmed `/status` and
  `/activity` show the PIN gate in a fresh context, render no content before unlock, and
  reveal their content after the PIN. The token-spending `node scripts/ui/surfaces.mjs` was
  **not run**.
- **Filed:** findings 3–7 and PIN throttling are tracked under
  `.scratch/security-pass/issues/`.

## Fixed point for review

Session start HEAD for this change is `a52d14b`. `/code-review a52d14b`.
