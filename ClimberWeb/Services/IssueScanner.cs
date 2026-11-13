using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>
/// Finds issues under <c>.scratch/**/issues/*.md</c> that are tracked but not yet
/// resolved, and reads their title and status. A missing <c>.scratch</c> tree is an
/// empty result, never an error.
/// </summary>
public static partial class IssueScanner
{
    private static readonly HashSet<string> ResolvedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "resolved",
        "closed",
        "done",
        "fixed",
        "complete",
        "completed",
    };

    [GeneratedRegex(@"^Status:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex TitleLine();

    /// <summary>Scans <paramref name="scratchRoot"/> for unresolved issues.</summary>
    public static IReadOnlyList<TrackedIssue> Scan(string scratchRoot)
    {
        if (!Directory.Exists(scratchRoot))
        {
            return Array.Empty<TrackedIssue>();
        }

        var issues = new List<TrackedIssue>();
        foreach (var file in Directory.EnumerateFiles(scratchRoot, "*.md", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(file);
            if (directory is null
                || !string.Equals(Path.GetFileName(directory), "issues", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var feature = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);
            if (Parse(content, feature, Path.GetFileName(file)) is { } issue)
            {
                issues.Add(issue);
            }
        }

        return issues
            .OrderBy(i => i.Feature, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Parses one issue file. Returns null when its status is resolved (so it drops out
    /// of the "tracked but not started" list).
    /// </summary>
    public static TrackedIssue? Parse(string content, string feature, string fileName)
    {
        var status = StatusLine().Match(content) is { Success: true } match
            ? match.Groups[1].Value.Trim()
            : "open";

        if (ResolvedStatuses.Contains(status))
        {
            return null;
        }

        var title = TitleLine().Match(content) is { Success: true } heading
            ? heading.Groups[1].Value.Trim()
            : Path.GetFileNameWithoutExtension(fileName);

        return new TrackedIssue(feature, fileName, title, status);
    }
}
