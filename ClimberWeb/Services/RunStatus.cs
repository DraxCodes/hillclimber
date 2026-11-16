namespace ClimberWeb.Services;

/// <summary>
/// Lifecycle state of a run, shared by the general OpenCode run and the hillclimb loop
/// so the dashboard can describe either with one vocabulary.
/// </summary>
public enum RunStatus
{
    /// <summary>No run has been started yet.</summary>
    Idle,

    /// <summary>The run is actively running.</summary>
    Running,

    /// <summary>The run finished normally.</summary>
    Completed,

    /// <summary>The run was aborted by the user (cancellation).</summary>
    Stopped,

    /// <summary>
    /// The run paused because the current provider failed. It resumes only after the user
    /// approves the next configured provider, or stops if they cancel.
    /// </summary>
    AwaitingApproval,

    /// <summary>The run terminated because of an unexpected error.</summary>
    Failed,
}



/// <summary>One vocabulary for run status labels, shared by the dashboard and activity page.</summary>
public static class RunStatusText
{
    /// <summary>A lower-case, human-readable label for <paramref name="status"/>.</summary>
    public static string Label(this RunStatus status) => status switch
    {
        RunStatus.Idle => "idle",
        RunStatus.Running => "running",
        RunStatus.Completed => "completed",
        RunStatus.Stopped => "stopped",
        RunStatus.AwaitingApproval => "awaiting approval",
        RunStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// The status dot's classes. One place so the sessions rail and the page header cannot
/// disagree about what "failed" looks like.
/// </summary>
public static class StatusDot
{
    /// <summary>The dot's colour classes for <paramref name="status"/>.</summary>
    /// <param name="status">The state the dot describes.</param>
    /// <param name="pulse">True for work in progress, which breathes; paused work does not.</param>
    public static string Class(RunStatus status, bool pulse = false) => status switch
    {
        RunStatus.Running => pulse ? "bg-primary animate-pulse" : "bg-primary",
        RunStatus.Completed => "bg-green",
        RunStatus.Stopped => "bg-orange",
        RunStatus.Failed => "bg-red",
        RunStatus.AwaitingApproval => "bg-orange",
        _ => "bg-muted-foreground/45",
    };
}

/// <summary>Which workflow the dashboard is driving.</summary>
public enum RunMethod
{
    /// <summary>A single general <c>opencode run</c>: no measurement, no revert.</summary>
    Run,

    /// <summary>The opt-in hill-climb loop: run, measure, keep or revert, repeat.</summary>
    Hillclimb,
}
