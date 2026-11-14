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
    /// The run paused because the primary provider failed. It resumes only after the user
    /// approves the OpenRouter fallback, or stops if they cancel.
    /// </summary>
    AwaitingApproval,

    /// <summary>The run terminated because of an unexpected error.</summary>
    Failed,
}

/// <summary>Which inference provider a run is using.</summary>
public enum AiProvider
{
    /// <summary>Surplus Intelligence, the primary provider.</summary>
    Surplus,

    /// <summary>OpenRouter, used only after explicit approval.</summary>
    OpenRouter,
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

/// <summary>Which workflow the dashboard is driving.</summary>
public enum RunMethod
{
    /// <summary>A single general <c>opencode run</c>: no measurement, no revert.</summary>
    Run,

    /// <summary>The opt-in hill-climb loop: run, measure, keep or revert, repeat.</summary>
    Hillclimb,
}
