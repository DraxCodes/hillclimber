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

    /// <summary>The run terminated because of an unexpected error.</summary>
    Failed,
}

/// <summary>Which workflow the dashboard is driving.</summary>
public enum RunMethod
{
    /// <summary>A single general <c>opencode run</c>: no measurement, no revert.</summary>
    Run,

    /// <summary>The opt-in hill-climb loop: run, measure, keep or revert, repeat.</summary>
    Hillclimb,
}
