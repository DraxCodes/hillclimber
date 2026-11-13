namespace ClimberWeb.Services;

/// <summary>
/// Pre-flight assessment of a climb target: whether a run can sensibly start, and the
/// non-blocking problems worth warning about beforehand.
/// </summary>
/// <param name="CanRun">False only for a missing/blank folder, where a run cannot start.</param>
/// <param name="Warnings">Human-readable problems to surface in the configure panel.</param>
public sealed record ClimbPreflightResult(bool CanRun, IReadOnlyList<string> Warnings);

/// <summary>Checks a target folder and the required tools before a climb starts.</summary>
public interface IClimbPreflight
{
    /// <summary>Assesses <paramref name="folder"/>. Never throws.</summary>
    ClimbPreflightResult Check(string? folder);
}

/// <summary>
/// Filesystem + <c>PATH</c> checks that catch the common ways a climb quietly fails:
/// a folder that does not exist, a target that is not a git repository (so losing
/// iterations cannot be reverted), and tools that are not installed.
/// </summary>
public sealed class ClimbPreflight : IClimbPreflight
{
    private static readonly string[] RequiredTools = ["opencode", "dotnet", "git"];

    /// <inheritdoc />
    public ClimbPreflightResult Check(string? folder)
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

        if (!LooksLikeGitRepository(folder))
        {
            warnings.Add("Target is not a git repository — a losing iteration cannot be reverted.");
        }

        foreach (var tool in RequiredTools)
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
