namespace ClimberWeb.Services;

/// <summary>
/// Pre-flight assessment of a climb target: whether a run can sensibly start, and the
/// non-blocking problems worth warning about beforehand.
/// </summary>
/// <param name="CanRun">False only for a missing/blank folder, where a run cannot start.</param>
/// <param name="Warnings">Human-readable problems to surface in the configure panel.</param>
public sealed record ClimbPreflightResult(bool CanRun, IReadOnlyList<string> Warnings);

/// <summary>Checks a target folder and the required tools before a run starts.</summary>
public interface IClimbPreflight
{
    /// <summary>
    /// Assesses <paramref name="folder"/> for the given <paramref name="method"/>. Never throws.
    /// Defaults to a hillclimb, which needs the git-repository check and every tool; a plain
    /// run needs only <c>opencode</c>.
    /// </summary>
    ClimbPreflightResult Check(string? folder, RunMethod method = RunMethod.Hillclimb);
}

/// <summary>
/// Filesystem + <c>PATH</c> checks that catch the common ways a run quietly fails:
/// a folder that does not exist, a hillclimb target that is not a git repository (so
/// losing iterations cannot be reverted), and tools that are not installed.
/// </summary>
public sealed class ClimbPreflight : IClimbPreflight
{
    private static readonly string[] RunTools = ["opencode"];
    private static readonly string[] HillclimbTools = ["opencode", "dotnet", "git"];

    /// <inheritdoc />
    public ClimbPreflightResult Check(string? folder, RunMethod method = RunMethod.Hillclimb)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new ClimbPreflightResult(false, ["Choose a target folder."]);
        }

        if (!Directory.Exists(folder))
        {
            return new ClimbPreflightResult(false, [$"Target folder does not exist: {folder}"]);
        }

        var warnings = new List<string>();

        if (method == RunMethod.Hillclimb && !LooksLikeGitRepository(folder))
        {
            warnings.Add("Target is not a git repository — a losing iteration cannot be reverted.");
        }

        foreach (var tool in method == RunMethod.Hillclimb ? HillclimbTools : RunTools)
        {
            if (ProcessCommandRunner.FindOnPath(tool) is null)
            {
                warnings.Add($"'{tool}' was not found on PATH.");
            }
        }

        return new ClimbPreflightResult(true, warnings);
    }

    private static bool LooksLikeGitRepository(string folder) =>
        Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));
}
