namespace ClimberWeb.Services;

/// <summary>The display primitives an <c>AiTurn</c> needs, independent of log parsing.</summary>
/// <param name="Summary">The one-line label, for example "Worked in 3 steps · 1 failed".</param>
/// <param name="Status">The turn's display state.</param>
/// <param name="Open">Whether the turn starts expanded: while streaming, or when a call failed.</param>
/// <param name="Footer">The human cost/token line from <c>step_finish</c>, or null while open.</param>
public sealed record TurnView(string Summary, StepStatus Status, bool Open, string? Footer);

/// <summary>
/// Turns a grouper turn into the primitives the reusable <c>Ai</c> components render. Pure:
/// the transcript and the tests agree on every summary.
/// </summary>
public static class TurnPresentation
{
    /// <summary>
    /// The rows to show for a tab. A row left <c>running</c> after its run stopped is history,
    /// not live work — the stream simply ends without a status — so it settles.
    /// </summary>
    /// <param name="entries">The tab's entries, in chronological order.</param>
    /// <param name="live">True while the run itself is still streaming.</param>
    public static IReadOnlyList<LogEntry> Settled(IReadOnlyList<LogEntry> entries, bool live)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return live ? entries : entries.Select(Settle).ToArray();
    }

    /// <summary>Drops the <c>running</c> status from a row whose run is no longer live.</summary>
    public static LogEntry Settle(LogEntry entry) =>
        entry.Status == StepStatus.Running ? entry with { Status = StepStatus.None } : entry;

    /// <summary>Builds the turn view for <paramref name="entries"/> and its optional footer.</summary>
    /// <param name="entries">The turn's reasoning and tool rows, in chronological order.</param>
    /// <param name="footer">The <c>step_finish</c> that closed the turn, or null.</param>
    /// <param name="open">True only for the feed's last turn, which never got a footer.</param>
    /// <param name="live">True while the run itself is still streaming.</param>
    public static TurnView For(
        IReadOnlyList<LogEntry> entries,
        LogEntry? footer,
        bool open = false,
        bool live = false)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var steps = 0;
        var thoughts = 0;
        var failed = 0;

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case LogKind.Tool:
                case LogKind.Subagent:
                    steps++;
                    break;
                case LogKind.Reasoning:
                    thoughts++;
                    break;
            }

            if (entry.Status == StepStatus.Failed)
            {
                failed++;
            }
        }

        // "Still working" is a claim about the run, not about the events: an unterminated
        // turn whose run has stopped was cut off, and must stop shimmering and settle.
        var streaming = open && live;
        var interrupted = open && !live;

        var verb = streaming ? "Working in" : interrupted ? "Stopped after" : "Worked in";
        var summary = steps > 0
            ? $"{verb} {steps} {(steps == 1 ? "step" : "steps")}"
            : thoughts > 0
                ? (streaming ? "Thinking…" : interrupted ? "Stopped while thinking" : "Thought")
                : streaming ? "Working…" : interrupted ? "Stopped" : "Worked";

        if (failed > 0)
        {
            summary += $" · {failed} failed";
        }

        return new TurnView(
            summary,
            failed > 0 ? StepStatus.Failed : streaming ? StepStatus.Running : StepStatus.None,
            Open: streaming || failed > 0,
            Footer: footer?.Text);
    }
}