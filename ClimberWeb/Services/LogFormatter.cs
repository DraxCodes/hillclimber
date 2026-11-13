using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>The shape of a rendered log line, after <see cref="LogFormatter"/> classification.</summary>
public enum LogKind
{
    /// <summary>Anything that is not a recognised opencode JSON event; rendered verbatim.</summary>
    Plain,

    /// <summary>An opencode <c>text</c> event; <see cref="LogEntry.Text"/> is the message.</summary>
    Text,

    /// <summary>An opencode <c>tool_use</c> event; tool name, input summary and output are populated.</summary>
    Tool,

    /// <summary>An opencode <c>step_start</c> event.</summary>
    StepStart,

    /// <summary>An opencode <c>step_finish</c> event.</summary>
    StepFinish,

    /// <summary>An opencode <c>error</c> event.</summary>
    Error,
}

/// <summary>A log line prepared for display.</summary>
/// <param name="Raw">The original line exactly as it lives in <c>ClimberState.Logs</c>.</param>
/// <param name="Timestamp">The <c>HH:mm:ss</c> stamp, or empty when the line carries none.</param>
/// <param name="Kind">How the line should be rendered.</param>
/// <param name="Text">
/// The primary display text: the message for <see cref="LogKind.Text"/>, the tool name for
/// <see cref="LogKind.Tool"/>, and the marker/summary for the step and error kinds.
/// </param>
/// <param name="ToolInput">One-line summary of the tool input; may be empty.</param>
/// <param name="ToolOutput">Tool output; may be null.</param>
public sealed record LogEntry(
    string Raw,
    string Timestamp,
    LogKind Kind,
    string Text,
    string? ToolInput = null,
    string? ToolOutput = null);

/// <summary>
/// Turns the run's log feed into display-ready <see cref="LogEntry"/> rows. Lines the
/// hill-climb loop writes itself, and <c>dotnet test</c> / <c>git</c> output, pass through
/// as <see cref="LogKind.Plain"/>. Lines emitted by <c>opencode run --format json</c> — one
/// long JSON object per event — are parsed so the panel shows what the event means instead
/// of its raw payload.
/// </summary>
/// <remarks>
/// Parsing is a render-only concern: the log feed in <c>ClimberState.Logs</c> is never modified.
/// Results are cached per line because the UI re-renders on every state change.
/// </remarks>
public sealed class LogFormatter
{
    private const int MaxCachedLines = 6000;
    private const int MaxSummaryLength = 100;

    private static readonly Regex StampRegex = new(@"^\[(\d{2}:\d{2}:\d{2})\]\s?", RegexOptions.Compiled);

    private readonly Dictionary<string, LogEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Classifies <paramref name="line"/>, caching the result (see <see cref="Parse"/>).</summary>
    public LogEntry Format(string line)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(line, out var cached))
            {
                return cached;
            }
        }

        var entry = Parse(line);

        lock (_gate)
        {
            if (_cache.Count >= MaxCachedLines)
            {
                _cache.Clear();
            }

            _cache[line] = entry;
        }

        return entry;
    }

    /// <summary>
    /// Classifies a single stamped log line. Never throws: malformed JSON, JSON that is not an
    /// opencode event, and unknown event types all fall back to <see cref="LogKind.Plain"/>.
    /// </summary>
    public static LogEntry Parse(string line)
    {
        line ??= string.Empty;
        var (timestamp, payload) = SplitStamp(line);
        var plain = new LogEntry(line, timestamp, LogKind.Plain, line);

        // Cheap guard: opencode events are JSON objects. Anything else skips the parser.
        var trimmed = payload.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return plain;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return plain;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("part", out var part)
                || part.ValueKind != JsonValueKind.Object)
            {
                return plain;
            }

            return typeElement.GetString() switch
            {
                "text" => TextEntry(plain, part),
                "tool_use" => ToolEntry(plain, part),
                "step_start" => plain with { Kind = LogKind.StepStart, Text = "step start" },
                "step_finish" => plain with { Kind = LogKind.StepFinish, Text = SummarizeFinish(part) },
                "error" => plain with { Kind = LogKind.Error, Text = SummarizeError(root, part) },
                _ => plain,
            };
        }
    }

    private static LogEntry TextEntry(LogEntry plain, JsonElement part)
    {
        var text = GetString(part, "text");
        return string.IsNullOrEmpty(text)
            ? plain
            : plain with { Kind = LogKind.Text, Text = text };
    }

    private static LogEntry ToolEntry(LogEntry plain, JsonElement part)
    {
        var tool = GetString(part, "tool");
        if (string.IsNullOrEmpty(tool))
        {
            return plain;
        }

        var hasState = part.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object;
        var input = hasState && state.TryGetProperty("input", out var rawInput) ? SummarizeInput(rawInput) : string.Empty;
        var output = hasState ? GetString(state, "output") : null;

        return plain with
        {
            Kind = LogKind.Tool,
            Text = tool,
            ToolInput = input,
            ToolOutput = output,
        };
    }

    private static string SummarizeFinish(JsonElement part)
    {
        var reason = GetString(part, "reason") ?? "stop";
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

        var summary = new StringBuilder()
            .Append("step finish · ").Append(reason)
            .Append(" · $").Append(cost.ToString("0.######"))
            .Append(" · in ").Append(input);

        if (cacheRead > 0 || cacheWrite > 0)
        {
            summary.Append(" (cache ").Append(cacheRead).Append(" read / ").Append(cacheWrite).Append(" write)");
        }

        summary.Append(" / out ").Append(output);

        if (reasoning > 0)
        {
            summary.Append(" · reasoning ").Append(reasoning);
        }

        return summary.ToString();
    }

    private static string SummarizeError(JsonElement root, JsonElement part)
    {
        var message = GetString(part, "error") ?? GetString(part, "message");

        if (string.IsNullOrWhiteSpace(message)
            && root.TryGetProperty("error", out var error)
            && error.ValueKind != JsonValueKind.Null)
        {
            message = error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText();
        }

        return string.IsNullOrWhiteSpace(message) ? "error" : Flatten(message);
    }

    private static string SummarizeInput(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var property in input.EnumerateObject())
        {
            if (parts.Count >= 3)
            {
                break;
            }

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };

            if (value is not null)
            {
                parts.Add($"{property.Name}={Truncate(Flatten(value), MaxSummaryLength)}");
            }
        }

        return string.Join(" · ", parts);
    }

    private static (string Timestamp, string Payload) SplitStamp(string line)
    {
        var match = StampRegex.Match(line);
        return match.Success
            ? (match.Groups[1].Value, line[match.Length..])
            : (string.Empty, line);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string Flatten(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}
