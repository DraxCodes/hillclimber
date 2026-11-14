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
/// <param name="Pricing">The model's pricing, or null when it could not be read.</param>
/// <param name="Message">Human-readable explanation when not connected, else null.</param>
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
    /// Finds <paramref name="model"/> in the catalogue and reads its pricing. Returns null
    /// for malformed JSON, an empty catalogue, or a model that is not listed.
    /// </summary>
    public static SurplusPricing? ParsePricing(string? json, string model)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var target = NormalizeModelId(model);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var idElement)
                    || idElement.ValueKind != JsonValueKind.String
                    || !string.Equals(idElement.GetString(), target, StringComparison.OrdinalIgnoreCase)
                    || !item.TryGetProperty("pricing", out var pricing)
                    || pricing.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                return new SurplusPricing(
                    ReadNumber(pricing, "prompt"),
                    ReadNumber(pricing, "completion"),
                    ReadNumber(pricing, "input_cache_read"),
                    ReadNumber(pricing, "input_cache_write"));
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static double ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0d;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0d,
        };
    }
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

            var pricing = SurplusParser.ParsePricing(body, _options.Model);
            if (pricing is null)
            {
                var message = $"Surplus did not list {_options.Model}; spend cannot be estimated.";
                notes.Add(message);
                return new SurplusReport(true, true, false, _options.Model, _options.FallbackModel, null, message, notes);
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