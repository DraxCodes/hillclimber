using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Services;

/// <summary>
/// Surplus Intelligence credentials and endpoint. The API key is only used to identify the
/// provider in the dashboard; inference itself is performed by <c>opencode</c>, which reads
/// the same key from its own environment.
/// </summary>
/// <param name="ApiKey">Surplus API key, or null when none is configured.</param>
/// <param name="BaseUrl">OpenAI-compatible API base.</param>
/// <param name="Model">Primary model id passed to <c>opencode run --model</c>.</param>
/// <param name="FallbackModel">OpenRouter model id offered after explicit approval.</param>
public sealed record SurplusOptions(string? ApiKey, string BaseUrl, string Model, string FallbackModel)
{
    /// <summary>Surplus's OpenAI-compatible API base.</summary>
    public const string DefaultBaseUrl = "https://api.surplusintelligence.ai/min80/v1";

    /// <summary>The primary model; the <c>-1017</c> suffix is part of Surplus's model id.</summary>
    public const string DefaultModel = "surplus/deepseek-v4.1-flash-1017";

    /// <summary>The OpenRouter model offered as an approved fallback.</summary>
    public const string DefaultFallbackModel = "openrouter/deepseek/deepseek-v4.1-flash";

    /// <summary>True when a Surplus API key is present.</summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Reads the options from configuration. Environment variables
    /// (<c>SURPLUS_API_KEY</c>, <c>Climber__SurplusApiKey</c>) and a gitignored
    /// <c>appsettings.local.json</c> both work; nothing here is committed.
    /// </summary>
    public static SurplusOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var baseUrl = configuration["Climber:SurplusBaseUrl"];
        var model = configuration["Climber:SurplusModel"];
        var fallback = configuration["Climber:FallbackModel"];

        return new SurplusOptions(
            FirstNonEmpty(configuration["Climber:SurplusApiKey"], configuration["SURPLUS_API_KEY"]),
            string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/'),
            NormalizeModel(string.IsNullOrWhiteSpace(model) ? DefaultModel : model, "surplus"),
            NormalizeModel(string.IsNullOrWhiteSpace(fallback) ? DefaultFallbackModel : fallback, "openrouter"));
    }

    /// <summary>
    /// Ensures a model id carries its provider prefix. Accepts a bare id from configuration
    /// (e.g. <c>deepseek-v4.1-flash-1017</c>) and leaves a prefixed id untouched.
    /// </summary>
    public static string NormalizeModel(string model, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var trimmed = model.Trim();
        if (trimmed.StartsWith(provider + "/", StringComparison.OrdinalIgnoreCase) && trimmed.Length > provider.Length + 1)
        {
            return trimmed;
        }

        // A different *serving* provider prefix is replaced; a model-family prefix such as
        // `deepseek/…` is kept and the serving provider is added in front of it.
        foreach (var other in new[] { "surplus/", "openrouter/" })
        {
            if (!string.Equals(other, provider + "/", StringComparison.OrdinalIgnoreCase)
                && trimmed.StartsWith(other, StringComparison.OrdinalIgnoreCase)
                && trimmed.Length > other.Length)
            {
                return $"{provider}/{trimmed[other.Length..]}";
            }
        }

        return $"{provider}/{trimmed}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        Array.Find(values, value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

/// <summary>Per-token Surplus pricing, read from <c>GET /models</c>. Values are USD per token.</summary>
/// <param name="Prompt">Input token price.</param>
/// <param name="Completion">Output token price.</param>
/// <param name="CacheRead">Prompt-cache read price.</param>
/// <param name="CacheWrite">Prompt-cache write price.</param>
public sealed record SurplusPricing(double Prompt, double Completion, double CacheRead, double CacheWrite)
{
    /// <summary>
    /// Estimates the USD cost of the supplied token counts. This is an approximation: it
    /// trusts <c>step_finish</c>'s token breakdown rather than a provider invoice.
    /// </summary>
    public decimal Estimate(long input, long output, long cacheRead, long cacheWrite) =>
        Math.Round(
            (decimal)(input * Prompt + output * Completion + cacheRead * CacheRead + cacheWrite * CacheWrite),
            8);
}

/// <summary>The dashboard's view of Surplus: configured, reachable, and priced.</summary>
/// <param name="Configured">True when an API key is present.</param>
/// <param name="Reachable">True when <c>GET /models</c> answered successfully.</param>
/// <param name="ModelAvailable">True when the configured model appears in the catalogue.</param>
/// <param name="Model">The configured primary model id.</param>
/// <param name="FallbackModel">The configured OpenRouter fallback id.</param>
/// <param name="Pricing">The model's pricing, or null when it is unlisted or publishes no usable pricing.</param>
/// <param name="Message">Human-readable explanation when not connected or unpriced, else null.</param>
/// <param name="Notes">Non-fatal explanations to show on the page.</param>
public sealed record SurplusReport(
    bool Configured,
    bool Reachable,
    bool ModelAvailable,
    string Model,
    string FallbackModel,
    SurplusPricing? Pricing,
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

/// <summary>Reads Surplus's model catalogue and pricing. Never throws.</summary>
public interface ISurplusClient
{
    /// <summary>
    /// Builds a <see cref="SurplusReport"/>. A missing key, a network failure, or an
    /// unexpected response degrade to an explicit status rather than an exception.
    /// </summary>
    Task<SurplusReport> GetReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of looking a model up in Surplus's catalogue.</summary>
/// <param name="Listed">True when the catalogue contains the model's id.</param>
/// <param name="Pricing">The model's per-token pricing, or null when it publishes none.</param>
public readonly record struct SurplusCatalogueEntry(bool Listed, SurplusPricing? Pricing);

/// <summary>Parsers for Surplus's <c>GET /models</c> response, shared by client and tests.</summary>
public static class SurplusParser
{
    /// <summary>Strips the <c>provider/</c> prefix and any <c>#variant</c> suffix.</summary>
    public static string NormalizeModelId(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var id = model.Trim();
        var slash = id.IndexOf('/');
        if (slash >= 0)
        {
            id = id[(slash + 1)..];
        }

        var hash = id.IndexOf('#');
        return hash >= 0 ? id[..hash] : id;
    }

    /// <summary>
    /// Looks <paramref name="model"/> up in the catalogue. A model that is listed without
    /// usable pricing comes back as listed with null <see cref="SurplusCatalogueEntry.Pricing"/>,
    /// so callers can tell "Surplus does not serve this model" from "Surplus serves it but
    /// cannot price it". A malformed or empty response is not listed.
    /// </summary>
    public static SurplusCatalogueEntry Lookup(string? json, string model)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        var target = NormalizeModelId(model);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return default;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var idElement)
                    || idElement.ValueKind != JsonValueKind.String
                    || !string.Equals(idElement.GetString(), target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Object)
                {
                    return new SurplusCatalogueEntry(true, null);
                }

                // Prompt and completion are the prices an estimate cannot do without; a cache
                // price that is absent may legitimately be free, so it defaults to zero.
                if (!TryReadNumber(pricing, "prompt", out var prompt)
                    || !TryReadNumber(pricing, "completion", out var completion))
                {
                    return new SurplusCatalogueEntry(true, null);
                }

                return new SurplusCatalogueEntry(
                    true,
                    new SurplusPricing(
                        prompt,
                        completion,
                        ReadNumber(pricing, "input_cache_read"),
                        ReadNumber(pricing, "input_cache_write")));
            }
        }
        catch (JsonException)
        {
            return default;
        }

        return default;
    }

    /// <summary>
    /// Reads a per-token price. A missing or non-finite value is not readable, so a caller
    /// that needs the price can treat the model as unpriced instead of pricing it at NaN.
    /// </summary>
    private static bool TryReadNumber(JsonElement element, string name, out double number)
    {
        number = 0d;
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out var parsed):
                number = parsed;
                return true;
            case JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                // A catalogue can say "NaN" or "Infinity"; either would poison every estimate.
                number = parsed;
                return double.IsFinite(parsed);
            default:
                return false;
        }
    }

    private static double ReadNumber(JsonElement element, string name) =>
        TryReadNumber(element, name, out var number) ? number : 0d;
}

/// <summary>
/// HTTP client for Surplus's model catalogue. Every call is defensive: a missing key, a
/// non-success status, or a network failure becomes an explicit status, never an exception.
/// </summary>
public sealed class SurplusClient : ISurplusClient
{
    private readonly HttpClient _http;
    private readonly SurplusOptions _options;

    /// <summary>Creates the client from DI-provided configuration.</summary>
    public SurplusClient(HttpClient http, IConfiguration configuration)
        : this(http, SurplusOptions.FromConfiguration(configuration))
    {
    }

    /// <summary>Creates the client with explicit options (used by tests).</summary>
    public SurplusClient(HttpClient http, SurplusOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<SurplusReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        if (!_options.HasKey)
        {
            notes.Add("Add a Surplus API key (Climber:SurplusApiKey or SURPLUS_API_KEY) to show connection state.");
            return new SurplusReport(false, false, false, _options.Model, _options.FallbackModel, null, "No Surplus API key is configured.", notes);
        }

        try
        {
            // /models is the provider's open catalogue; the key is only a configured/not-configured
            // signal, so it is deliberately not sent to an endpoint that does not need it.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl}/models");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = $"Surplus returned HTTP {(int)response.StatusCode} for its model catalogue.";
                notes.Add(message);
                return new SurplusReport(true, false, false, _options.Model, _options.FallbackModel, null, message, notes);
            }

            var entry = SurplusParser.Lookup(body, _options.Model);
            if (!entry.Listed)
            {
                var message = $"Surplus did not list {_options.Model}; spend cannot be estimated.";
                notes.Add(message);
                return new SurplusReport(true, true, false, _options.Model, _options.FallbackModel, null, message, notes);
            }

            if (entry.Pricing is not { } pricing)
            {
                // The model works, it just cannot be priced; that is not a reason to fall back.
                var message = $"Surplus lists {_options.Model} but publishes no pricing for it; spend cannot be estimated.";
                notes.Add(message);
                return new SurplusReport(true, true, true, _options.Model, _options.FallbackModel, null, message, notes);
            }

            return new SurplusReport(true, true, true, _options.Model, _options.FallbackModel, pricing, null, notes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var message = $"Could not reach Surplus: {ex.Message}";
            notes.Add(message);
            return new SurplusReport(true, false, false, _options.Model, _options.FallbackModel, null, message, notes);
        }
    }
}