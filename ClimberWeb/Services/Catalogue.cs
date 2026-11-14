using System.Globalization;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>Per-token pricing, read from an OpenAI-compatible <c>GET /models</c>. Values are USD per token.</summary>
/// <param name="Prompt">Input token price.</param>
/// <param name="Completion">Output token price.</param>
/// <param name="CacheRead">Prompt-cache read price.</param>
/// <param name="CacheWrite">Prompt-cache write price.</param>
public sealed record ProviderPricing(double Prompt, double Completion, double CacheRead, double CacheWrite)
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

/// <summary>Result of looking a model up in a provider's catalogue.</summary>
/// <param name="Listed">True when the catalogue contains the model's id.</param>
/// <param name="Pricing">The model's per-token pricing, or null when it publishes none.</param>
public readonly record struct CatalogueEntry(bool Listed, ProviderPricing? Pricing);

/// <summary>Parsers for an OpenAI-compatible <c>GET /models</c> response, shared by probe and tests.</summary>
public static class OpenAiCatalogueParser
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
    /// usable pricing comes back as listed with null <see cref="CatalogueEntry.Pricing"/>,
    /// so callers can tell "the provider does not serve this model" from "it serves it but
    /// cannot price it". A malformed or empty response is not listed.
    /// </summary>
    public static CatalogueEntry Lookup(string? json, string model)
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
                    return new CatalogueEntry(true, null);
                }

                // Prompt and completion are the prices an estimate cannot do without; a cache
                // price that is absent may legitimately be free, so it defaults to zero.
                if (!TryReadNumber(pricing, "prompt", out var prompt)
                    || !TryReadNumber(pricing, "completion", out var completion))
                {
                    return new CatalogueEntry(true, null);
                }

                return new CatalogueEntry(
                    true,
                    new ProviderPricing(
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