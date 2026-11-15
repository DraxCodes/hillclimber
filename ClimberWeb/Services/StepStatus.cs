namespace ClimberWeb.Services;

/// <summary>
/// The completion state of one step in a run — a tool call or a subagent call. Read from
/// opencode's <c>part.state.status</c>; the transcript renders it as a running shimmer, a
/// red failure, or a plain done row.
/// </summary>
public enum StepStatus
{
    /// <summary>Done, or no status was reported.</summary>
    None,

    /// <summary>Still running; the UI shows a running step.</summary>
    Running,

    /// <summary>Failed; the UI shows an error step with its output opened.</summary>
    Failed,
}