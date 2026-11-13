using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Services;

/// <summary>
/// Builds the <see cref="StatusReport"/> behind <c>/status</c>: git history is read
/// live, while coverage/test status comes from the committed artifact. Every step is
/// defensive — a missing repository, git binary, or artifact becomes a warning on the
/// report, never an exception.
/// </summary>
public sealed class ProjectStatusService : IProjectStatusService
{
    private const int MaxCommits = 200;

    private readonly ICommandRunner _runner;
    private readonly IConfiguration _configuration;
    private readonly string _startDirectory;

    /// <summary>Creates the service using the app base directory to find the repository root.</summary>
    public ProjectStatusService(ICommandRunner runner, IConfiguration configuration)
        : this(runner, configuration, AppContext.BaseDirectory)
    {
    }

    /// <summary>Creates the service with an explicit directory to start the root search from.</summary>
    public ProjectStatusService(ICommandRunner runner, IConfiguration configuration, string startDirectory)
    {
        _runner = runner;
        _configuration = configuration;
        _startDirectory = startDirectory;
    }

    /// <inheritdoc />
    public async Task<StatusReport> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var root = ResolveRepoRoot(_configuration["Climber:RepoRoot"], _startDirectory);

        if (root is null)
        {
            warnings.Add("Could not locate the repository root; showing the workflow only.");
            return new StatusReport(
                null,
                Array.Empty<ChangeEntry>(),
                CadenceUnit.Day,
                Array.Empty<CadenceBucket>(),
                null,
                Array.Empty<TrackedIssue>(),
                new WorkflowCatalog("unknown", WorkflowCatalogReader.BaseWorkflow, Array.Empty<WorkflowRole>()),
                warnings);
        }

        var sidecar = ChangelogSidecar.ReadFile(Path.Combine(root, ChangelogSidecar.RelativePath));
        var changes = await ReadChangelogAsync(root, sidecar, warnings, cancellationToken).ConfigureAwait(false);
        var (unit, buckets) = GitHistory.BuildCadence(changes);
        var coverage = CoverageArtifact.ReadFile(Path.Combine(root, CoverageArtifact.RelativePath));
        var issues = IssueScanner.Scan(Path.Combine(root, ".scratch"));
        var workflow = WorkflowCatalogReader.Read(root);
        var repo = await ReadRepoAsync(root, warnings, cancellationToken).ConfigureAwait(false);

        return new StatusReport(repo, changes, unit, buckets, coverage, issues, workflow, warnings);
    }

    /// <summary>
    /// Resolves the repository root: the configured path when set, otherwise the nearest
    /// ancestor of <paramref name="startDirectory"/> that holds a <c>.git</c> entry or a
    /// solution file.
    /// </summary>
    public static string? ResolveRepoRoot(string? configured, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        for (var current = directory; current is not null; current = current.Parent)
        {
            if (HasGitEntry(current.FullName) || HasSolution(current.FullName))
            {
                return current.FullName;
            }
        }

        return null;
    }

    private static bool HasGitEntry(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    private static bool HasSolution(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.slnx").Any();
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<ChangeEntry>> ReadChangelogAsync(
        string root,
        IReadOnlyDictionary<string, string> sidecar,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var result = await GitAsync(
            root,
            ["log", "-n", MaxCommits.ToString(), $"--pretty=format:{GitHistory.PrettyFormat}"],
            cancellationToken).ConfigureAwait(false);

        if (result is null || result.ExitCode != 0)
        {
            warnings.Add("git history is unavailable; the changelog and cadence are empty.");
            return Array.Empty<ChangeEntry>();
        }

        var changes = GitHistory.ParseLog(result.StandardOutput, sidecar);
        if (changes.Count == 0)
        {
            warnings.Add("No commits found — a shallow clone or empty repository has no history to show.");
        }

        return changes;
    }

    private async Task<RepoSnapshot?> ReadRepoAsync(
        string root,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var head = await GitAsync(root, ["rev-parse", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (head is null || head.ExitCode != 0)
        {
            warnings.Add("HEAD could not be resolved; is this a git repository?");
            return null;
        }

        var branch = await GitAsync(root, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        var status = await GitAsync(root, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);

        return new RepoSnapshot(
            root,
            branch is { ExitCode: 0 } ? branch.StandardOutput.Trim() : "detached",
            head.StandardOutput.Trim(),
            status is { ExitCode: 0 } && status.StandardOutput.Trim().Length > 0);
    }

    private async Task<CommandResult?> GitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runner
                .RunAsync(new CommandRequest("git", arguments, root), null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
