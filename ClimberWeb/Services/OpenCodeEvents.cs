using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>Cost and token counts reported by an opencode <c>step_finish</c> event.</summary>
/// <param name="Cost">Cost in USD for the step, as reported by opencode.</param>
/// <param name="Input">Input tokens.</param>
/// <param name="Output">Output tokens.</param>
/// <param name="Reasoning">Reasoning tokens.</param>
/// <param name="CacheRead">Prompt-cache read tokens.</param>
/// <param name="CacheWrite">Prompt-cache write tokens.</param>
public sealed record StepUsage(
    double Cost,
    int Input,
    int Output,
    int Reasoning,
    int CacheRead,
    int CacheWrite);

/// <summary>
/// Reads the usage numbers out of opencode's JSON event stream. Shared by the log
/// renderer (which shows them) and the climb loop (which sums their cost), so the two
/// never disagree about what a <c>step_finish</c> contains.
/// </summary>
public static class OpenCodeEvents
{
    /// <summary>
    /// Reads the cost and tokens from a <c>step_finish</c> event's <c>part</c> object.
    /// Never throws: a missing or non-numeric field reads as zero.
    /// </summary>
    public static StepUsage ReadUsage(JsonElement part)
    {
        var cost = part.TryGetProperty("cost", out var costElement) && costElement.ValueKind == JsonValueKind.Number
            ? costElement.GetDouble()
            : 0d;

        var input = 0;
        var output = 0;
        var reasoning = 0;
        var cacheRead = 0;
        var cacheWrite = 0;

        if (part.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            input = GetInt(tokens, "input");
            output = GetInt(tokens, "output");
            reasoning = GetInt(tokens, "reasoning");

            if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
            {
                cacheRead = GetInt(cache, "read");
                cacheWrite = GetInt(cache, "write");
            }
        }

        return new StepUsage(cost, input, output, reasoning, cacheRead, cacheWrite);
    }

    /// <summary>
    /// Reads the usage of a raw feed line when it is an opencode <c>step_finish</c> event,
    /// or null for any other line. Tolerates the <c>[HH:mm:ss] </c> stamp and malformed
    /// JSON without throwing.
    /// </summary>
    public static StepUsage? TryReadStepUsage(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var start = line.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line[start..]);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "step_finish", StringComparison.Ordinal)
                || !root.TryGetProperty("part", out var part)
                || part.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadUsage(part);
        }
    }

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
