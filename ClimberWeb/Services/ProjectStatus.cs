namespace ClimberWeb.Services;

/// <summary>Bucket size used by the iteration-cadence graph.</summary>
public enum CadenceUnit
{
    Hour,
    Day,
}

/// <summary>Point-in-time state of the repository the page describes.</summary>
/// <param name="Root">Absolute path of the repository root.</param>
/// <param name="Branch">Current branch name, or a detached-HEAD marker.</param>
/// <param name="Head">Short commit hash of HEAD.</param>
/// <param name="Dirty">True when the worktree has uncommitted changes.</param>
public sealed record RepoSnapshot(string Root, string Branch, string Head, bool Dirty);

/// <summary>
/// One commit in the changelog, with its simplified one-line description.
/// </summary>
/// <param name="Hash">Short commit hash.</param>
/// <param name="When">Author timestamp.</param>
/// <param name="RawSubject">The commit subject exactly as recorded by git.</param>
/// <param name="Description">The simplified one-line description shown on the page.</param>
/// <param name="Authored">
/// True when <paramref name="Description"/> came from an explicit source (a
/// <c>Description:</c> trailer or the sidecar); false when it was derived from the subject.
/// </param>
public sealed record ChangeEntry(
    string Hash,
    DateTimeOffset When,
    string RawSubject,
    string Description,
    bool Authored);

/// <summary>A single column of the cadence graph.</summary>
/// <param name="Start">Start of the bucket (local time).</param>
/// <param name="Label">Short axis label, e.g. <c>14:00</c> or <c>Nov 13</c>.</param>
/// <param name="Count">Commits in the bucket.</param>
public sealed record CadenceBucket(DateTimeOffset Start, string Label, int Count);

/// <summary>Coverage and test status read from the committed artifact.</summary>
/// <param name="Generated">When the artifact was produced, if recorded.</param>
/// <param name="Passed">Passing tests.</param>
/// <param name="Failed">Failing tests.</param>
/// <param name="Skipped">Skipped tests.</param>
/// <param name="LineRate">Line coverage in the range 0..1, or null when unknown.</param>
public sealed record CoverageSummary(
    DateTimeOffset? Generated,
    int Passed,
    int Failed,
    int Skipped,
    double? LineRate)
{
    /// <summary>Total tests that produced a result.</summary>
    public int Total => Passed + Failed + Skipped;

    /// <summary>True when nothing failed.</summary>
    public bool Passing => Failed == 0;

    /// <summary>Line coverage as a whole number percentage, or null when unknown.</summary>
    public int? LinePercent => LineRate is { } rate ? (int)Math.Round(rate * 100) : null;
}

/// <summary>A tracked issue that has not been resolved yet.</summary>
/// <param name="Feature">The <c>.scratch/&lt;feature&gt;</c> directory the issue lives in.</param>
/// <param name="Number">The leading number of the issue file name, or 0 when it has none.</param>
/// <param name="FileName">File name of the issue markdown.</param>
/// <param name="Title">The issue's first <c>#</c> heading.</param>
/// <param name="Status">The value of the issue's <c>Status:</c> line.</param>
public sealed record TrackedIssue(string Feature, int Number, string FileName, string Title, string Status);

/// <summary>One agent role in the workflow, from its front-matter.</summary>
/// <param name="Name">Agent id, e.g. <c>plan-architect</c>.</param>
/// <param name="Description">One-line description from the front-matter.</param>
/// <param name="Model">Model id from the front-matter, or empty when unset.</param>
/// <param name="ReadOnly">True when the agent denies edits.</param>
/// <param name="Mode">Front-matter <c>mode</c> (e.g. <c>primary</c>, <c>subagent</c>), or empty when unset.</param>
/// <param name="DisplayName">Friendly name for the UI (front-matter <c>display_name</c>, else a title-cased id).</param>
public sealed record WorkflowRole(string Name, string Description, string Model, bool ReadOnly, string Mode, string DisplayName)
{
    /// <summary>True when the front-matter marks the agent <c>mode: primary</c>.</summary>
    public bool IsPrimary => string.Equals(Mode, "primary", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The models in play and how a change is produced.</summary>
/// <param name="BaseModel">The default model from <c>opencode.json</c>.</param>
/// <param name="BaseWorkflow">Short description of the everyday loop.</param>
/// <param name="Roles">The agent trio, when their configs are present.</param>
public sealed record WorkflowCatalog(
    string BaseModel,
    string BaseWorkflow,
    IReadOnlyList<WorkflowRole> Roles);

/// <summary>Everything the status page renders, in one immutable snapshot.</summary>
/// <param name="Repo">Repository state, or null when the root could not be resolved.</param>
/// <param name="Changes">Changelog rows, newest first.</param>
/// <param name="CadenceUnit">Bucket size used by the cadence graph.</param>
/// <param name="Cadence">Cadence buckets, oldest first, contiguous.</param>
/// <param name="Coverage">Coverage/test summary, or null when not measured yet.</param>
/// <param name="Issues">Tracked-but-unresolved issues.</param>
/// <param name="Workflow">Models and workflow.</param>
/// <param name="Warnings">Non-fatal problems to surface on the page.</param>
public sealed record StatusReport(
    RepoSnapshot? Repo,
    IReadOnlyList<ChangeEntry> Changes,
    CadenceUnit CadenceUnit,
    IReadOnlyList<CadenceBucket> Cadence,
    CoverageSummary? Coverage,
    IReadOnlyList<TrackedIssue> Issues,
    WorkflowCatalog Workflow,
    IReadOnlyList<string> Warnings);

/// <summary>Reads the project's status from git and committed artifacts.</summary>
public interface IProjectStatusService
{
    /// <summary>
    /// Builds a <see cref="StatusReport"/>. Never throws: any failure is reported as a
    /// warning on the returned snapshot.
    /// </summary>
    Task<StatusReport> GetStatusAsync(CancellationToken cancellationToken = default);
}
