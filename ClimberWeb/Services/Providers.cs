using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Services;

/// <summary>How a run's spend is known for a provider.</summary>
public enum SpendMode
{
    /// <summary>Neither an estimate nor a reported cost is available.</summary>
    None,

    /// <summary>Spend is estimated from tokens times catalogue pricing.</summary>
    Estimated,

    /// <summary>The provider/opencode reports the exact cost.</summary>
    Reported,
}

/// <summary>The built-in provider ids, kept as constants so call sites stay typo-safe.</summary>
public static class ProviderIds
{
    /// <summary>Surplus Intelligence, the shipped primary provider.</summary>
    public const string Surplus = "surplus";

    /// <summary>OpenRouter, the shipped approval-gated fallback.</summary>
    public const string OpenRouter = "openrouter";
}

/// <summary>The standard adapters a provider can speak.</summary>
public static class ProviderKinds
{
    /// <summary>An OpenAI-compatible <c>GET /models</c> catalogue.</summary>
    public const string OpenAi = "openai";

    /// <summary>OpenRouter's management (<c>/key</c>, <c>/credits</c>, <c>/activity</c>) surface.</summary>
    public const string OpenRouter = "openrouter";

    /// <summary>No API surface; opencode handles the provider on its own.</summary>
    public const string None = "none";
}

/// <summary>A configured inference provider: identity, defaults and keys.</summary>
/// <param name="Id">Stable id used in configuration, run state and the model prefix.</param>
/// <param name="DisplayName">Human label shown in the dashboard.</param>
/// <param name="ModelPrefix">Serving-provider prefix opencode expects in front of the model.</param>
/// <param name="Model">Model id passed to <c>opencode run --model</c>, or null to let opencode decide.</param>
/// <param name="BaseUrl">API base for the provider's standard surface, or null when it has none.</param>
/// <param name="ApiKey">Inference/catalogue key, or null when the provider needs none.</param>
/// <param name="ManagementApiKey">Management key for account/activity endpoints, when required.</param>
/// <param name="SpendMode">Whether spend is estimated, reported, or unknown for this provider.</param>
/// <param name="RequiresKey">False for a local OpenAI-compatible host that needs no key.</param>
/// <param name="SendKey">False for a catalogue endpoint that does not accept or need the key.</param>
/// <param name="Kind">Which standard adapter(s) the provider speaks: <c>openai</c>, <c>openrouter</c>, or <c>none</c>.</param>
public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    string ModelPrefix,
    string? Model,
    string? BaseUrl,
    string? ApiKey,
    string? ManagementApiKey,
    SpendMode SpendMode,
    bool RequiresKey = true,
    bool SendKey = true,
    string Kind = ProviderKinds.None)
{
    /// <summary>True when an inference/catalogue key is configured.</summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>True when a management key is configured.</summary>
    public bool HasManagementKey => !string.IsNullOrWhiteSpace(ManagementApiKey);
}

/// <summary>The dashboard's view of a provider's catalogue connection.</summary>
/// <param name="Configured">True when the provider is configured enough to probe.</param>
/// <param name="Reachable">True when the catalogue endpoint answered successfully.</param>
/// <param name="ModelAvailable">True when the configured model appears in the catalogue (or none is configured).</param>
/// <param name="Model">The configured model id, if any.</param>
/// <param name="Pricing">The model's pricing, or null when unlisted or unpublished.</param>
/// <param name="Message">Human-readable explanation when not connected or unpriced, else null.</param>
/// <param name="Notes">Non-fatal explanations to show on the page.</param>
public sealed record ProviderReport(
    bool Configured,
    bool Reachable,
    bool ModelAvailable,
    string? Model,
    ProviderPricing? Pricing,
    string? Message,
    IReadOnlyList<string> Notes)
{
    /// <summary>Short human label for the connection state.</summary>
    public string StateLabel =>
        !Configured ? "not configured"
        : Reachable && ModelAvailable ? "connected"
        : Reachable ? "model unavailable"
        : "unreachable";

    /// <summary>CSS tone for the state dot: <c>ok</c>, <c>bad</c>, or <c>off</c>.</summary>
    public string StateTone =>
        !Configured ? "off" : Reachable && ModelAvailable ? "ok" : "bad";
}

/// <summary>Reads an OpenAI-compatible model catalogue. Never throws.</summary>
public interface IProviderCatalogue
{
    /// <summary>Builds a <see cref="ProviderReport"/>; failures degrade to an explicit status.</summary>
    Task<ProviderReport> GetReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads a provider's management-API spend surface. Never throws.</summary>
public interface IProviderSpend
{
    /// <summary>Builds a <see cref="SpendReport"/>; failures degrade to an explicit status.</summary>
    Task<SpendReport> GetSpendAsync(CancellationToken cancellationToken = default);
}

/// <summary>A provider plus whichever standard API surfaces it supports.</summary>
/// <param name="Descriptor">The configured provider.</param>
/// <param name="Catalogue">OpenAI-compatible catalogue probe, or null when the provider has none.</param>
/// <param name="Spend">Management-API spend surface, or null when the provider has none.</param>
public sealed record RegisteredProvider(
    ProviderDescriptor Descriptor,
    IProviderCatalogue? Catalogue = null,
    IProviderSpend? Spend = null)
{
    /// <summary>The provider's stable id.</summary>
    public string Id => Descriptor.Id;

    /// <summary>The provider's human label.</summary>
    public string DisplayName => Descriptor.DisplayName;

    /// <summary>Whether spend is estimated, reported, or unknown.</summary>
    public SpendMode SpendMode => Descriptor.SpendMode;
}

/// <summary>One concrete provider/model pair used for a run step.</summary>
/// <param name="Id">Provider id.</param>
/// <param name="DisplayName">Provider label used in messages.</param>
/// <param name="Model">Model id, or null to let opencode decide.</param>
/// <param name="SpendMode">How this step's spend is known.</param>
/// <param name="Pricing">Catalogue pricing for a token-based estimate, or null.</param>
public sealed record ProviderSelection(
    string Id,
    string DisplayName,
    string? Model,
    SpendMode SpendMode,
    ProviderPricing? Pricing = null)
{
    /// <summary>Builds a selection from a registered provider.</summary>
    public static ProviderSelection From(RegisteredProvider provider, ProviderPricing? pricing = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new ProviderSelection(
            provider.Id,
            provider.DisplayName,
            provider.Descriptor.Model,
            provider.Descriptor.SpendMode,
            pricing);
    }
}

/// <summary>The ordered provider list the app resolves configuration into.</summary>
public interface IProviderRegistry
{
    /// <summary>Providers in configured order; the first is primary.</summary>
    IReadOnlyList<RegisteredProvider> Providers { get; }

    /// <summary>Non-fatal configuration problems (unknown kind, duplicate id, ...).</summary>
    IReadOnlyList<string> Notes { get; }

    /// <summary>The first configured provider, or null when none is valid.</summary>
    RegisteredProvider? Primary { get; }

    /// <summary>Finds a provider by id, case-insensitively; null falls back to the primary.</summary>
    RegisteredProvider? Find(string? id);

    /// <summary>Normalises a model id onto <paramref name="providerId"/>'s serving prefix.</summary>
    string? NormalizeModel(string providerId, string? model);

    /// <summary>The providers after <paramref name="providerId"/> as run selections, in order.</summary>
    IReadOnlyList<ProviderSelection> FallbacksAfter(string providerId);
}

/// <summary>
/// Builds the provider list from <c>Climber:Providers</c>, or synthesises the legacy
/// Surplus/OpenRouter pair from the flat <c>Climber:Surplus*</c>/<c>Climber:OpenRouter*</c>
/// keys and their environment variables. The array order is the fallback order.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    /// <summary>Surplus's OpenAI-compatible API base.</summary>
    public const string SurplusDefaultBaseUrl = "https://api.surplusintelligence.ai/min80/v1";

    /// <summary>Surplus's default primary model; the <c>-1017</c> suffix is part of its id.</summary>
    public const string SurplusDefaultModel = "surplus/deepseek-v4.1-flash-1017";

    /// <summary>OpenRouter's public API base.</summary>
    public const string OpenRouterDefaultBaseUrl = "https://openrouter.ai/api/v1";

    /// <summary>OpenRouter's default fallback model.</summary>
    public const string OpenRouterDefaultModel = "openrouter/deepseek/deepseek-v4.1-flash";

    private readonly IReadOnlyList<RegisteredProvider> _providers;

    private ProviderRegistry(IReadOnlyList<RegisteredProvider> providers, IReadOnlyList<string> notes)
    {
        _providers = providers;
        Notes = notes;
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredProvider> Providers => _providers;

    /// <inheritdoc />
    public IReadOnlyList<string> Notes { get; }

    /// <inheritdoc />
    public RegisteredProvider? Primary => _providers.Count > 0 ? _providers[0] : null;

    /// <inheritdoc />
    public RegisteredProvider? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Primary;
        }

        return _providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public string? NormalizeModel(string providerId, string? model)
    {
        var provider = Find(providerId) ?? throw new ArgumentException(
            $"No provider with id '{providerId}' is configured.", nameof(providerId));

        return NormalizeModel(model, provider.Descriptor.ModelPrefix, Prefixes());
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderSelection> FallbacksAfter(string providerId)
    {
        var index = -1;
        for (var i = 0; i < _providers.Count; i++)
        {
            if (string.Equals(_providers[i].Id, providerId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || index == _providers.Count - 1)
        {
            return Array.Empty<ProviderSelection>();
        }

        return _providers.Skip(index + 1).Select(provider => ProviderSelection.From(provider)).ToArray();
    }

    /// <summary>Builds the registry from configuration and the shared HTTP client.</summary>
    public static ProviderRegistry FromConfiguration(IConfiguration configuration, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(http);

        var notes = new List<string>();
        var entries = configuration.GetSection("Climber:Providers").GetChildren().ToList();
        var raw = entries.Count > 0
            ? ReadEntries(entries, configuration, notes)
            : ReadLegacy(configuration);

        var prefixes = raw.Select(descriptor => descriptor.ModelPrefix)
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var providers = new List<RegisteredProvider>(raw.Count);
        foreach (var descriptor in raw)
        {
            if (providers.Any(provider => string.Equals(provider.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add($"Provider '{descriptor.Id}' is configured more than once; keeping the first entry.");
                continue;
            }

            var model = descriptor.Kind == ProviderKinds.None
                ? descriptor.Model?.Trim()
                : NormalizeModel(descriptor.Model, descriptor.ModelPrefix, prefixes);
            var normalised = descriptor with { Model = model };
            providers.Add(new RegisteredProvider(
                normalised,
                CreateCatalogue(normalised, http),
                CreateSpend(normalised, http)));
        }

        return new ProviderRegistry(providers, notes);
    }

    /// <summary>
    /// Ensures a model id carries its serving-provider prefix, replacing a different known
    /// serving prefix while keeping a model-family prefix such as <c>deepseek/…</c>.
    /// </summary>
    public static string? NormalizeModel(string? model, string prefix, IEnumerable<string> servingPrefixes)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var trimmed = model.Trim();
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return trimmed;
        }

        if (trimmed.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        foreach (var other in servingPrefixes)
        {
            if (!string.Equals(other, prefix, StringComparison.OrdinalIgnoreCase)
                && trimmed.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)
                && trimmed.Length > other.Length + 1)
            {
                return $"{prefix}/{trimmed[(other.Length + 1)..]}";
            }
        }

        return $"{prefix}/{trimmed}";
    }

    private string[] Prefixes() => _providers
        .Select(provider => provider.Descriptor.ModelPrefix)
        .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IProviderCatalogue? CreateCatalogue(ProviderDescriptor descriptor, HttpClient http) =>
        descriptor.Kind == ProviderKinds.OpenAi ? new OpenAiCatalogueProbe(http, descriptor) : null;

    private static IProviderSpend? CreateSpend(ProviderDescriptor descriptor, HttpClient http) =>
        descriptor.Kind == ProviderKinds.OpenRouter ? new OpenRouterSpendSurface(http, descriptor) : null;

    private static List<ProviderDescriptor> ReadLegacy(IConfiguration configuration)
    {
        var surplusBaseUrl = FirstNonEmpty(configuration["Climber:SurplusBaseUrl"]) ?? SurplusDefaultBaseUrl;
        var surplusModel = FirstNonEmpty(configuration["Climber:SurplusModel"]) ?? SurplusDefaultModel;
        var fallbackModel = FirstNonEmpty(configuration["Climber:FallbackModel"]) ?? OpenRouterDefaultModel;
        var openRouterBaseUrl = FirstNonEmpty(configuration["Climber:OpenRouterBaseUrl"]) ?? OpenRouterDefaultBaseUrl;

        return
        [
            new ProviderDescriptor(
                ProviderIds.Surplus,
                "Surplus",
                ProviderIds.Surplus,
                surplusModel,
                surplusBaseUrl.TrimEnd('/'),
                FirstNonEmpty(configuration["Climber:SurplusApiKey"], configuration["SURPLUS_API_KEY"]),
                ManagementApiKey: null,
                SpendMode.Estimated,
                RequiresKey: true,
                SendKey: false,
                Kind: ProviderKinds.OpenAi),
            new ProviderDescriptor(
                ProviderIds.OpenRouter,
                "OpenRouter",
                ProviderIds.OpenRouter,
                fallbackModel,
                openRouterBaseUrl.TrimEnd('/'),
                FirstNonEmpty(configuration["Climber:OpenRouterApiKey"], configuration["OPENROUTER_API_KEY"]),
                FirstNonEmpty(configuration["Climber:OpenRouterManagementApiKey"], configuration["OPENROUTER_MANAGEMENT_API_KEY"]),
                SpendMode.Reported,
                Kind: ProviderKinds.OpenRouter),
        ];
    }

    private static List<ProviderDescriptor> ReadEntries(
        IEnumerable<IConfigurationSection> entries,
        IConfiguration configuration,
        List<string> notes)
    {
        var descriptors = new List<ProviderDescriptor>();

        foreach (var entry in entries)
        {
            var id = entry["Id"]?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                notes.Add("A Climber:Providers entry has no Id and was ignored.");
                continue;
            }

            var kind = entry["Kind"]?.Trim().ToLowerInvariant() ?? ProviderKinds.None;
            if (kind is not (ProviderKinds.OpenAi or ProviderKinds.OpenRouter or ProviderKinds.None))
            {
                notes.Add($"Provider '{id}' has unknown kind '{kind}'; it will run without an API surface.");
                kind = ProviderKinds.None;
            }

            var keyPrefix = id.ToUpperInvariant().Replace('-', '_').Replace('.', '_');
            var baseUrl = FirstNonEmpty(entry["BaseUrl"])
                ?? (id.Equals(ProviderIds.Surplus, StringComparison.OrdinalIgnoreCase) ? SurplusDefaultBaseUrl
                    : id.Equals(ProviderIds.OpenRouter, StringComparison.OrdinalIgnoreCase) ? OpenRouterDefaultBaseUrl
                    : null);
            var model = FirstNonEmpty(entry["Model"])
                ?? (id.Equals(ProviderIds.Surplus, StringComparison.OrdinalIgnoreCase) ? SurplusDefaultModel
                    : id.Equals(ProviderIds.OpenRouter, StringComparison.OrdinalIgnoreCase) ? OpenRouterDefaultModel
                    : null);

            var descriptor = new ProviderDescriptor(
                id,
                FirstNonEmpty(entry["Name"]) ?? DisplayNameFor(id),
                FirstNonEmpty(entry["Prefix"]) ?? id,
                model,
                baseUrl?.TrimEnd('/'),
                FirstNonEmpty(entry["ApiKey"], configuration[$"{keyPrefix}_API_KEY"]),
                FirstNonEmpty(entry["ManagementApiKey"], configuration[$"{keyPrefix}_MANAGEMENT_API_KEY"]),
                ParseSpendMode(entry["Spend"], kind),
                ReadBool(entry["RequiresKey"], fallback: kind != ProviderKinds.None),
                ReadBool(entry["SendKey"], fallback: true),
                kind);

            if (descriptor.RequiresKey && !descriptor.HasKey)
            {
                notes.Add($"Provider '{id}' has no API key; it will show as not configured until one is supplied.");
            }

            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    private static SpendMode ParseSpendMode(string? value, string kind) => value?.Trim().ToLowerInvariant() switch
    {
        "estimated" => SpendMode.Estimated,
        "reported" => SpendMode.Reported,
        "none" => SpendMode.None,
        _ => kind switch
        {
            ProviderKinds.OpenAi => SpendMode.Estimated,
            ProviderKinds.OpenRouter => SpendMode.Reported,
            _ => SpendMode.None,
        },
    };

    private static bool ReadBool(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static string DisplayNameFor(string id) =>
        string.Join(' ', id.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static string? FirstNonEmpty(params string?[] values) =>
        Array.Find(values, value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>
/// Reads an OpenAI-compatible <c>GET /models</c> catalogue. Any OpenAI-compatible host can
/// use this adapter; the provider's display name and key requirements come from its descriptor.
/// </summary>
public sealed class OpenAiCatalogueProbe : IProviderCatalogue
{
    private readonly HttpClient _http;
    private readonly ProviderDescriptor _descriptor;

    /// <summary>Creates the probe with an explicit descriptor (used by the registry and tests).</summary>
    public OpenAiCatalogueProbe(HttpClient http, ProviderDescriptor descriptor)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    /// <inheritdoc />
    public async Task<ProviderReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var name = _descriptor.DisplayName;

        if (_descriptor.RequiresKey && !_descriptor.HasKey)
        {
            notes.Add($"Add a {name} API key (Climber:Providers:<id>:ApiKey or a provider-specific environment variable) to show connection state.");
            return new ProviderReport(false, false, false, _descriptor.Model, null, $"No {name} API key is configured.", notes);
        }

        if (string.IsNullOrWhiteSpace(_descriptor.BaseUrl))
        {
            notes.Add($"Provider '{_descriptor.Id}' has no BaseUrl; connection state cannot be checked.");
            return new ProviderReport(false, false, false, _descriptor.Model, null, $"{name} has no API base configured.", notes);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_descriptor.BaseUrl}/models");
            if (_descriptor.SendKey && _descriptor.HasKey)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _descriptor.ApiKey);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = $"{name} returned HTTP {(int)response.StatusCode} for its model catalogue.";
                notes.Add(message);
                return new ProviderReport(true, false, false, _descriptor.Model, null, message, notes);
            }

            if (string.IsNullOrWhiteSpace(_descriptor.Model))
            {
                // No model to check: the catalogue answered, and opencode's own config picks the model.
                return new ProviderReport(true, true, true, null, null, null, notes);
            }

            var entry = OpenAiCatalogueParser.Lookup(body, _descriptor.Model);
            if (!entry.Listed)
            {
                var message = $"{name} did not list {_descriptor.Model}; spend cannot be estimated.";
                notes.Add(message);
                return new ProviderReport(true, true, false, _descriptor.Model, null, message, notes);
            }

            if (entry.Pricing is not { } pricing)
            {
                var message = $"{name} lists {_descriptor.Model} but publishes no pricing for it; spend cannot be estimated.";
                notes.Add(message);
                return new ProviderReport(true, true, true, _descriptor.Model, null, message, notes);
            }

            return new ProviderReport(true, true, true, _descriptor.Model, pricing, null, notes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var message = $"Could not reach {name}: {ex.Message}";
            notes.Add(message);
            return new ProviderReport(true, false, false, _descriptor.Model, null, message, notes);
        }
    }
}