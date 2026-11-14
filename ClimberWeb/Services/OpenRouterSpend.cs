using System.Globalization;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>State of an OpenRouter-style provider's connection, from <c>GET /key</c>.</summary>
/// <param name="Configured">True when a normal API key is configured at all.</param>
/// <param name="Connected">True when the key authenticated successfully.</param>
/// <param name="KeyLabel">The key's label, e.g. <c>sk-or-v1-…890</c>.</param>
/// <param name="IsFreeTier">Whether the provider reports the key as free tier.</param>
/// <param name="Usage">Key usage in USD, if reported.</param>
/// <param name="Limit">Key spend limit in USD, if any.</param>
/// <param name="LimitRemaining">Remaining allowance in USD, if any.</param>
/// <param name="Message">Human-readable explanation when not connected, else null.</param>
public sealed record ProviderConnection(
    bool Configured,
    bool Connected,
    string? KeyLabel,
    bool IsFreeTier,
    double? Usage,
    double? Limit,
    double? LimitRemaining,
    string? Message)
{
    /// <summary>Short human label for the connection state.</summary>
    public string StateLabel => !Configured ? "not configured" : Connected ? "connected" : "not connected";

    /// <summary>CSS tone for the connection dot: <c>ok</c>, <c>bad</c>, or <c>off</c>.</summary>
    public string StateTone => !Configured ? "off" : Connected ? "ok" : "bad";
}

/// <summary>Account-wide credit totals from <c>GET /credits</c>.</summary>
/// <param name="TotalCredits">Total credits purchased, USD.</param>
/// <param name="TotalUsage">Total credits used, USD.</param>
public sealed record ProviderAccount(double TotalCredits, double TotalUsage)
{
    /// <summary>Credits left, floored at zero.</summary>
    public double Remaining => Math.Max(0d, TotalCredits - TotalUsage);
}

/// <summary>One day/model row of a provider's management activity.</summary>
/// <param name="Date">UTC date of the activity.</param>
/// <param name="Model">Model slug, e.g. <c>openai/gpt-4.1</c>.</param>
/// <param name="Requests">Requests on that model and day.</param>
/// <param name="PromptTokens">Prompt tokens.</param>
/// <param name="CompletionTokens">Completion tokens.</param>
/// <param name="Usage">Cost in USD.</param>
public sealed record ProviderActivityRow(
    DateOnly Date,
    string Model,
    int Requests,
    long PromptTokens,
    long CompletionTokens,
    double Usage);

/// <summary>Everything the spend surfaces render, in one immutable snapshot.</summary>
/// <param name="Connection">Connection state.</param>
/// <param name="Account">Account totals, or null when no management key / on failure.</param>
/// <param name="Activity">Activity rows (may be empty), oldest first.</param>
/// <param name="Notes">Non-fatal explanations to surface on the page.</param>
public sealed record SpendReport(
    ProviderConnection Connection,
    ProviderAccount? Account,
    IReadOnlyList<ProviderActivityRow> Activity,
    IReadOnlyList<string> Notes);

/// <summary>Result of parsing <c>GET /key</c>.</summary>
/// <param name="Label">Key label.</param>
/// <param name="IsFreeTier">Whether the key is free tier.</param>
/// <param name="Usage">Key usage in USD.</param>
/// <param name="Limit">Key limit in USD.</param>
/// <param name="LimitRemaining">Remaining allowance in USD.</param>
public sealed record OpenRouterKey(
    string? Label,
    bool IsFreeTier,
    double? Usage,
    double? Limit,
    double? LimitRemaining);

/// <summary>
/// Pure parsers for OpenRouter's management JSON, shared by the client and its unit tests.
/// Every method tolerates missing fields and malformed JSON by returning null/empty.
/// </summary>
public static class OpenRouterParser
{
    /// <summary>Parses a <c>/key</c> response body, or null when unusable.</summary>
    public static OpenRouterKey? ParseKey(string? json)
    {
        var data = ReadDataObject(json);
        if (data is not { } key)
        {
            return null;
        }

        return new OpenRouterKey(
            TryGetString(key, "label"),
            TryGetBool(key, "is_free_tier") ?? false,
            TryGetDouble(key, "usage"),
            TryGetDouble(key, "limit"),
            TryGetDouble(key, "limit_remaining"));
    }

    /// <summary>Parses a <c>/credits</c> response body, or null when unusable.</summary>
    public static ProviderAccount? ParseCredits(string? json)
    {
        var data = ReadDataObject(json);
        if (data is not { } credits
            || TryGetDouble(credits, "total_credits") is not { } purchased
            || TryGetDouble(credits, "total_usage") is not { } used)
        {
            return null;
        }

        return new ProviderAccount(purchased, used);
    }

    /// <summary>Parses an <c>/activity</c> response body into rows, oldest first.</summary>
    public static IReadOnlyList<ProviderActivityRow> ParseActivity(string? json)
    {
        if (!TryReadRoot(json, out var root)
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProviderActivityRow>();
        }

        var rows = new List<ProviderActivityRow>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !DateOnly.TryParseExact(TryGetString(item, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            rows.Add(new ProviderActivityRow(
                date,
                TryGetString(item, "model") ?? "unknown",
                (int)(TryGetLong(item, "requests") ?? 0),
                TryGetLong(item, "prompt_tokens") ?? 0,
                TryGetLong(item, "completion_tokens") ?? 0,
                TryGetDouble(item, "usage") ?? 0d));
        }

        return rows.OrderBy(row => row.Date).ToArray();
    }

    private static JsonElement? ReadDataObject(string? json)
    {
        if (!TryReadRoot(json, out var root)
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return data;
    }

    private static bool TryReadRoot(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            // Parse and clone so the element outlives the disposed document.
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? TryGetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? TryGetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static long? TryGetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;
}