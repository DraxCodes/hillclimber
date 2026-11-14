# Spec: generic AI-provider support

## Context

The dashboard speaks to exactly two inference providers: Surplus Intelligence (primary) and
OpenRouter (the approval-gated fallback). Provider identity is an `AiProvider { Surplus,
OpenRouter }` enum, and provider-specific branching is spread across the run services, the
spend formatter, both pages, and the paid-test harness. Adding a third provider today means
editing the enum, every switch, the Razor markup, and the tests.

The goal is to make a provider a configuration entry plus (optionally) one or two standard
adapters, so a new provider that speaks a mainstream standard can be plugged in without
touching the run loop or the UI.

## Decisions (locked with the user)

1. **Descriptor + capabilities, not a fat interface.** A provider is a
   `ProviderDescriptor` (identity, display name, model prefix/model, endpoint, keys, spend
   mode) plus zero or more capability objects. An `opencode`-only provider is a descriptor
   with no capabilities and needs no API code.
2. **`Climber:Providers[]` with legacy aliases.** The array is the new configuration. When
   it is absent, the registry synthesises the same two entries from today's
   `Climber:Surplus*` / `Climber:OpenRouter*` keys and the `SURPLUS_API_KEY` /
   `OPENROUTER_API_KEY` / `OPENROUTER_MANAGEMENT_API_KEY` environment variables. Existing
   setups keep working.
3. **Ordered list, approval-gated.** Providers are evaluated in configured order. The first
   is primary; any later provider is a fallback and is only used after explicit approval.
   With two configured the UX is unchanged; with three, failures can advance down the list
   one approved step at a time.
4. **The UI renders the registry.** The spend line, provider bar, approval copy, and
   `/activity` provider cards are generated from the configured providers. A capability
   decides whether a pricing or management-spend panel appears.
5. **String ids replace the enum.** `surplus` and `openrouter` are constants; every other
   provider is just its configured id. Run/completion state carries a `ProviderSelection`.
6. **Spend mode is provider-independent.** `None`, `Estimated` (token × catalogue price) or
   `Reported` (exact cost). Mixed runs still show `est. X + $Y`; a provider with neither
   shows `—`, never `est. $0`.
7. **The paid harness is pinned to Surplus.** `surfaces.mjs` now requires a configuration
   with Surplus as the only provider, refuses (exit 2) otherwise or when Surplus is not
   connected, and keeps the echoed `--model surplus/…` / no-`openrouter/` / no-approval
   assertions. A free `providers.mjs` check covers the dynamic two-provider bar and
   approval flow without starting a run.
8. **No `/hillclimb`.** This is a behaviour-preserving refactor with no single honest
   measurable objective; it is implemented in atomic commits, verified with the real
   `dotnet test`, and reviewed with `/code-review`.

## Configuration shape

```json
{
  "Climber": {
    "Providers": [
      {
        "Id": "surplus",
        "Name": "Surplus",
        "Kind": "openai",
        "Prefix": "surplus",
        "Model": "surplus/deepseek-v4.1-flash-1017",
        "BaseUrl": "https://api.surplusintelligence.ai/min80/v1",
        "ApiKey": "",
        "RequiresKey": true,
        "SendKey": false
      },
      {
        "Id": "openrouter",
        "Name": "OpenRouter",
        "Kind": "openrouter",
        "Prefix": "openrouter",
        "Model": "openrouter/deepseek/deepseek-v4.1-flash",
        "BaseUrl": "https://openrouter.ai/api/v1",
        "ApiKey": "",
        "ManagementApiKey": ""
      }
    ]
  }
}
```

| Field | Default | Purpose |
|-------|---------|---------|
| `Id` | — (required) | Identity used in run state, config, and `opencode` model prefix. |
| `Name` | title-cased `Id` | Human label in the UI. |
| `Kind` | `none` | Adapter: `openai` (OpenAI-compatible `GET /models`), `openrouter` (management key/credits/activity), `none` (opencode-only). |
| `Prefix` | `Id` | Serving-provider prefix for `--model` normalisation. |
| `Model` | built-in default for `surplus`/`openrouter`, else null | Model id; null omits `--model` so opencode's own config decides. |
| `BaseUrl` | built-in default for `surplus`/`openrouter` | API base; trailing slash trimmed. |
| `ApiKey` | — | Inference/catalogue key. Also read from `{ID}_API_KEY`. |
| `ManagementApiKey` | — | Management key for OpenRouter's account/activity endpoints. |
| `RequiresKey` | `true` (`false` for `none`) | Set false for a local OpenAI-compatible host that needs no key. |
| `SendKey` | `true` | Set false for Surplus, whose catalogue does not need the key. |
| `Spend` | derived from `Kind` | Optional override: `estimated`, `reported`, `none`. |

Legacy keys remain aliases and are read when `Climber:Providers` is empty:

- `Climber:SurplusApiKey` / `SURPLUS_API_KEY`, `Climber:SurplusBaseUrl`, `Climber:SurplusModel`
- `Climber:OpenRouterApiKey` / `OPENROUTER_API_KEY`,
  `Climber:OpenRouterManagementApiKey` / `OPENROUTER_MANAGEMENT_API_KEY`,
  `Climber:OpenRouterBaseUrl`
- `Climber:FallbackModel` supplies OpenRouter's model.

## Model

```text
ProviderDescriptor  Id, DisplayName, ModelPrefix, Model, BaseUrl, ApiKey,
                    ManagementApiKey, SpendMode, RequiresKey, SendKey
RegisteredProvider  Descriptor + IProviderCatalogue? + IProviderSpend?
ProviderSelection   Id, DisplayName, Model, SpendMode, Pricing?   (one run step)
ProviderRegistry    ordered RegisteredProvider list, Primary, Find, NormalizeModel,
                    FallbacksAfter
SpendMode           None | Estimated | Reported
```

Capabilities:

- `IProviderCatalogue.GetReportAsync()` → `ProviderReport`
  (`Configured`, `Reachable`, `ModelAvailable`, `Model`, `Pricing`, `Message`, `Notes`).
  Implemented once by `OpenAiCatalogueProbe` for any OpenAI-compatible catalogue.
- `IProviderSpend.GetSpendAsync()` → `SpendReport`
  (`ProviderConnection`, `ProviderAccount`, `ProviderActivityRow`, `Notes`). Implemented by
  `OpenRouterSpendSurface`.

`ProviderPricing` (renamed from `SurplusPricing`) stays the OpenAI-style per-token shape.
`CatalogueEntry` (renamed from `SurplusCatalogueEntry`) keeps "listed but unpriced"
distinct from "not listed".

## Behaviour

- **Ordering.** The array order is the priority. `FallbacksAfter(id)` returns the providers
  after `id`, each as a `ProviderSelection` with no pricing. A run carries its current
  provider plus the remaining fallbacks; on failure it offers the next one by name, and
  approval advances one step. Exit code 127 (`opencode` missing) never offers a fallback.
- **Start gate.** Before launching, the selected provider's catalogue is refreshed. If it
  has a catalogue capability and is not reachable/model-available, the next provider is
  offered explicitly ("Start this run on X instead?"). Providers with no API surface start
  directly. Nothing switches silently.
- **Spend.** `SpendFormat.Spend/SpendLine` take a `SpendMode` and an optional display name.
  `Estimated` keeps the `est.` label (including `est. $0` when a priced provider has no
  usable pricing); `None`/`Reported` with no figures render `—` / "no cost reported".
  `RunMetrics` still sums reported cost and the token estimate separately.
- **UI.** The spend line lists every configured provider's state; the provider bar has one
  button per provider (primary active by default, non-primary asks for approval); the
  approval copy names the provider. `/activity` renders one card per provider: connection,
  model chip, pricing (catalogue), key/account/activity (spend surface), notes, and a
  session estimate panel; providers without an API surface say so.
- **Paid tests.** `surfaces.mjs` refuses unless the provider bar contains exactly
  `surplus`; it waits for Surplus's `.conn` state to read `connected`, never approves a
  fallback, and checks each echoed command line uses `surplus/` and not `openrouter/`.

## Out of scope

- Persisting runs or per-provider session attribution.
- A management-spend adapter for providers other than OpenRouter's API shape.
- Changing opencode's own provider configuration.
- Any paid test against OpenRouter.

## How we know it is done

- With no new config, the app behaves exactly as before: Surplus primary, OpenRouter
  fallback, the same defaults and legacy env vars.
- A `Climber:Providers` array with one `none` provider renders and runs without touching
  code; the provider bar and `/activity` show it.
- The two-provider UI still asks before OpenRouter and resets to Surplus after a run.
- `dotnet test --nologo` passes with tests for: legacy synthesis, array parsing, capability
  assignment, unknown kind, model normalisation, fallback ordering, approval advance, and
  every `SpendMode`.
- `surfaces.mjs` still exits 2 when Surplus is not connected and still proves any paid run
  used Surplus.