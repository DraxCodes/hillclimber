# Surplus Intelligence Harness Demo

This is a demo of my Surplus Intelligence harness.

- Hillclimber is a single-user, local-only Blazor Server dashboard on .NET 11 for driving OpenCode CLI runs against a project folder.
- It offers two modes: a one-off "run" and a gated "hillclimb" loop that iterates on a measurable objective and keeps the best result.
- The solution is split into the ClimberWeb app, its ClimberWeb.Tests project, and a fixtures/simple-climb sample, all tied together by HillClimberUI.slnx.
- The .opencode/ folder defines the task-orchestrator, plan-architect, quality-gatekeeper, and security-auditor agents, plus the /hillclimb and /handoff commands.
- Providers come from an ordered Climber:Providers registry, with Surplus Intelligence as the default and OpenRouter as an approval-gated fallback, and the app binds to LAN port 5179 behind a PIN.
