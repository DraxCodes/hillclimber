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

    /// <summary>
    /// An opencode <c>subagent</c> tool call; <see cref="LogEntry.Text"/> is the agent name,
    /// the input is its prompt, and the output is its (unwrapped) result.
    /// </summary>
    Subagent,

    /// <summary>An opencode <c>step_start</c> event.</summary>
    StepStart,

    /// <summary>An opencode <c>step_finish</c> event.</summary>
    StepFinish,

    /// <summary>An opencode <c>error</c> event.</summary>
    Error,
}

/// <summary>The completion state of a tool call, read from opencode's <c>part.state.status</c>.</summary>
public enum ToolStatus
{
    /// <summary>Completed successfully, or no status was reported.</summary>
    None,

    /// <summary>Still running; the UI shows a running step.</summary>
    Running,

    /// <summary>Failed; the UI shows an error step with its output opened.</summary>
    Failed,
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
/// <param name="ToolOutput">Tool output, or the error text when a tool failed; may be null.</param>
/// <param name="Status">The tool's completion state; <see cref="ToolStatus.None"/> for non-tools.</param>
public sealed record LogEntry(
    string Raw,
    string Timestamp,
    LogKind Kind,
    string Text,
    string? ToolInput = null,
    string? ToolOutput = null,
    ToolStatus Status = ToolStatus.None);

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
    private const int MaxPromptLength = 500;

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
        var input = hasState && state.TryGetProperty("input", out var rawInput) ? rawInput : default;
        var output = hasState ? GetString(state, "output") : null;
        var error = hasState ? GetString(state, "error") : null;
        var status = hasState ? ReadStatus(state) : ToolStatus.None;

        // A failed call often carries only `state.error`; keep it as the expandable body.
        var detail = string.IsNullOrEmpty(output) ? error : output;

        if (string.Equals(tool, "subagent", StringComparison.Ordinal))
        {
            return SubagentEntry(plain, input, detail, status);
        }

        return plain with
        {
            Kind = LogKind.Tool,
            Text = tool,
            ToolInput = SummarizeInput(input),
            ToolOutput = detail,
            Status = status,
        };
    }

    /// <summary>Maps opencode's tool <c>state.status</c> onto the display status.</summary>
    private static ToolStatus ReadStatus(JsonElement state) => GetString(state, "status") switch
    {
        "running" or "pending" or "in_progress" => ToolStatus.Running,
        "error" or "failed" => ToolStatus.Failed,
        _ => ToolStatus.None,
    };

    /// <summary>
    /// A <c>subagent</c> call: the agent name, its (flattened) prompt, and its result with
    /// opencode's <c>&lt;subagent …&gt;…&lt;/subagent&gt;</c> wrapper removed.
    /// </summary>
    private static LogEntry SubagentEntry(LogEntry plain, JsonElement input, string? output, ToolStatus status)
    {
        var isObject = input.ValueKind == JsonValueKind.Object;
        var agent = isObject ? GetString(input, "agent") : null;
        var prompt = isObject ? GetString(input, "prompt") : null;
        var result = StripSubagentWrapper(output);

        return plain with
        {
            Kind = LogKind.Subagent,
            Text = string.IsNullOrEmpty(agent) ? "subagent" : agent,
            ToolInput = string.IsNullOrEmpty(prompt) ? null : Truncate(Flatten(prompt), MaxPromptLength),
            ToolOutput = string.IsNullOrEmpty(result) ? null : result,
            Status = status,
        };
    }

    /// <summary>Removes the <c>&lt;subagent …&gt;…&lt;/subagent&gt;</c> wrapper from a subagent result.</summary>
    private static string? StripSubagentWrapper(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        var value = output.Trim();
        if (value.StartsWith("<subagent", StringComparison.Ordinal))
        {
            var open = value.IndexOf('>');
            if (open >= 0)
            {
                value = value[(open + 1)..];
            }
        }

        var close = value.LastIndexOf("</subagent>", StringComparison.Ordinal);
        if (close >= 0)
        {
            value = value[..close];
        }

        return value.Trim();
    }

    private static string SummarizeFinish(JsonElement part)
    {
        var reason = GetString(part, "reason") ?? "stop";
        var usage = OpenCodeEvents.ReadUsage(part);

        var summary = new StringBuilder()
            .Append("step finish · ").Append(reason)
            .Append(" · $").Append(usage.Cost.ToString("0.######"))
            .Append(" · in ").Append(usage.Input);

        if (usage.CacheRead > 0 || usage.CacheWrite > 0)
        {
            summary.Append(" (cache ").Append(usage.CacheRead).Append(" read / ").Append(usage.CacheWrite).Append(" write)");
        }

        summary.Append(" / out ").Append(usage.Output);

        if (usage.Reasoning > 0)
        {
            summary.Append(" · reasoning ").Append(usage.Reasoning);
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

    private static string Flatten(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}
