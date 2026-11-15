namespace ClimberWeb.Services;

/// <summary>What the output panel can say about the run right now.</summary>
/// <param name="Label">A short phrase for the header, for example "dotnet test" or "thinking".</param>
/// <param name="Working">True while the run is live; false when the panel is at rest.</param>
public sealed record ActivityView(string Label, bool Working);

/// <summary>
/// Derives the two live signals the output surface needs from the log itself: the panel's
/// "working" phrase, and whether a subagent's tab is the delegate the orchestrator is
/// currently waiting on. Pure and cheap, so the page can call it on every render and the
/// tests can pin every state without a browser.
/// </summary>
public static class OutputActivity
{
    /// <summary>
    /// The run's current activity, read from the newest log entry. A step_finish, a
    /// completed tool or a quiet line all mean the same thing to a reader — the run is
    /// between events but still working — so they fall back to the neutral phrase rather
    /// than pretending the work stopped.
    /// </summary>
    /// <param name="entries">The whole feed in chronological order.</param>
    /// <param name="live">True while the run is actually streaming.</param>
    public static ActivityView For(IReadOnlyList<LogEntry> entries, bool live)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!live)
        {
            return new ActivityView("idle", false);
        }

        if (entries.Count == 0)
        {
            return new ActivityView("starting", true);
        }

        return new ActivityView(LabelFor(entries[^1]), true);
    }

    /// <summary>
    /// True when <paramref name="agentId"/>'s tab is the work in flight: the newest entry
    /// in the whole feed is that agent's subagent row, and the run is live. The delegate's
    /// own row is the pointer the orchestrator is blocked on until another event arrives,
    /// so this is a claim about the stream's shape rather than a guess from colour.
    /// A failed delegate is not working, and never pulses.
    /// </summary>
    /// <param name="agentId">The agent id a subagent tab is keyed by.</param>
    /// <param name="entries">The whole feed in chronological order.</param>
    /// <param name="live">True while the run is actually streaming.</param>
    public static bool TabWorking(string agentId, IReadOnlyList<LogEntry> entries, bool live)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(entries);

        if (!live || entries.Count == 0)
        {
            return false;
        }

        var newest = entries[^1];
        return newest.Kind == LogKind.Subagent
            && string.Equals(newest.Text, agentId, StringComparison.Ordinal)
            && newest.Status != StepStatus.Failed;
    }

    private static string LabelFor(LogEntry entry) => entry.Kind switch
    {
        LogKind.Reasoning => "thinking",
        LogKind.Text => "writing",

        // The header already says the run is live, so the row's own "Running: " prefix only
        // spends the width the phrase needs: "Check the working tree" fits where
        // "Running: Check the working tree" would clip.
        LogKind.Tool when entry.Status == StepStatus.Running =>
            StripState(ToolPresentation.For(entry, primaryTab: true).Title),

        // The delegate's name is on its tab, where the dot pulses; the header names the act
        // so it stays short for every agent name.
        LogKind.Subagent when entry.Status == StepStatus.Running => "delegating",
        _ => "working",
    };

    private static string StripState(string title)
    {
        if (title.StartsWith("Running: ", StringComparison.Ordinal))
        {
            return title["Running: ".Length..];
        }

        return title.StartsWith("Running ", StringComparison.Ordinal)
            ? title["Running ".Length..]
            : title;
    }
}