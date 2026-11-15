namespace ClimberWeb.Services;

/// <summary>The display primitives a <c>AiTraceLine</c> needs, independent of log parsing.</summary>
/// <param name="Icon">One of: terminal, file, pencil, search, globe, subagent, gear.</param>
/// <param name="Title">The one-line label, for example "Ran dotnet test".</param>
/// <param name="Description">Optional secondary text, for example a leftover input key.</param>
/// <param name="Status">The step's display state.</param>
/// <param name="Detail">Expandable output, or null for a row with nothing to expand.</param>
public sealed record TraceView(
    string Icon,
    string Title,
    string? Description,
    StepStatus Status,
    string? Detail);

/// <summary>
/// Turns a tool or subagent <see cref="LogEntry"/> into the primitives the reusable
/// <c>Ai</c> components render. Pure: the transcript and the tests agree on every title.
/// </summary>
public static class ToolPresentation
{
    private const int MaxTitleLength = 80;

    private static readonly string[] ShellKeys = ["command", "cmd"];
    private static readonly string[] PathKeys = ["filePath", "path", "file"];
    private static readonly string[] SearchKeys = ["pattern", "query"];
    private static readonly string[] UrlKeys = ["url"];

    /// <summary>
    /// Builds the trace view for <paramref name="entry"/>. A subagent on the primary tab is a
    /// one-line pointer to its own tab; on its own tab it shows the prompt and result.
    /// </summary>
    public static TraceView For(LogEntry entry, bool primaryTab)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind == LogKind.Subagent
            ? SubagentView(entry, primaryTab)
            : ToolView(entry);
    }

    private static TraceView SubagentView(LogEntry entry, bool primaryTab)
    {
        var status = entry.Status;
        var agent = string.IsNullOrEmpty(entry.Text) || entry.Text == "subagent" ? null : entry.Text;

        // Status is carried in text, not only in colour: a failed subagent must read as
        // failed on both the primary tab and its own tab. The state leads the title so a
        // narrow row can clip the tail without hiding it.
        if (primaryTab)
        {
            var title = $"{StatePrefix(status)}{agent ?? "subagent"} — see its tab";
            return new TraceView("subagent", title, null, status, null);
        }

        var tabTitle = $"{StatePrefix(status)}{(agent is null ? "Ran subagent" : $"{agent} subagent")}";
        return new TraceView("subagent", tabTitle, entry.ToolInput, status, entry.ToolOutput);
    }

    /// <summary>Formats a non-done state as a title prefix, or an empty string for done.</summary>
    private static string StatePrefix(StepStatus status) => status switch
    {
        StepStatus.Running => "Running: ",
        StepStatus.Failed => "Failed: ",
        _ => string.Empty,
    };

    private static TraceView ToolView(LogEntry entry)
    {
        var (icon, verb, action, progressive, keys) = entry.Text switch
        {
            "shell" or "bash" => ("terminal", "Ran", "run", "Running", ShellKeys),
            "read" => ("file", "Read", "read", "Reading", PathKeys),
            "edit" or "write" or "patch" => ("pencil", "Edited", "edit", "Editing", PathKeys),
            "glob" or "grep" or "list" => ("search", "Searched", "search", "Searching", SearchKeys),
            "webfetch" => ("globe", "Fetched", "fetch", "Fetching", UrlKeys),
            _ => ("gear", "Ran", "run", "Running", Array.Empty<string>()),
        };

        var (subject, remainder) = SplitInput(entry.ToolInput, keys);
        var status = entry.Status;

        var title = status switch
        {
            StepStatus.Running => subject is null ? $"Running {entry.Text}" : $"{progressive} {subject}",
            StepStatus.Failed => subject is null ? $"Failed to run {entry.Text}" : $"Failed to {action} {subject}",
            _ => subject is null ? $"Ran {entry.Text}" : $"{verb} {subject}",
        };

        var description = subject is null
            ? (string.IsNullOrWhiteSpace(entry.ToolInput) ? null : entry.ToolInput)
            : remainder;

        var detail = string.IsNullOrWhiteSpace(entry.ToolOutput) ? null : entry.ToolOutput;

        return new TraceView(icon, Truncate(title, MaxTitleLength), description, status, detail);
    }

    /// <summary>
    /// Pulls the first recognized <c>key=value</c> segment out of the formatter's one-line input
    /// summary. Returns the value and whatever segments remain, or a null value and the whole
    /// summary when no known key is present.
    /// </summary>
    private static (string? Value, string? remainder) SplitInput(string? input, string[] keys)
    {
        if (string.IsNullOrEmpty(input) || keys.Length == 0)
        {
            return (null, string.IsNullOrEmpty(input) ? null : input);
        }

        // SummarizeInput joins keys with " · "; a value containing that separator is already
        // ambiguous in the one-line summary, so the first match wins.
        var segments = input.Split(" · ", StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var separator = segments[index].IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = segments[index][..separator];
            if (!keys.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            var value = segments[index][(separator + 1)..];
            var remainder = index == 0 && segments.Length == 1
                ? null
                : string.Join(" · ", segments.Where((_, position) => position != index));

            return (string.IsNullOrEmpty(value) ? null : value, string.IsNullOrEmpty(remainder) ? null : remainder);
        }

        return (null, input);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}
