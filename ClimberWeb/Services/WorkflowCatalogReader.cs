using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>
/// Reads the models-and-workflow section: the base model from <c>opencode.json</c>
/// and the agent roles from <c>.opencode/agents/*.md</c> front-matter. Missing files
/// fall back to sensible defaults rather than failing.
/// </summary>
public static partial class WorkflowCatalogReader
{
    /// <summary>Short description of the everyday, non-hillclimb workflow.</summary>
    public const string BaseWorkflow =
        "Everyday loop: describe the goal → a short spec under .scratch/<feature>/spec.md → implement → /code-review <fixed-point>.";

    /// <summary>Description of the opt-in hillclimb workflow, shown with the agent trio.</summary>
    public const string HillclimbWorkflow =
        "Opt-in hillclimb: task-orchestrator owns the run, plan-architect sets the metric and gates, quality-gatekeeper passes or fails each iteration.";

    private const string FallbackModel = "unknown";

    [GeneratedRegex(@"^\s*model\s*:\s*""?([^""\r\n]+)""?", RegexOptions.Multiline)]
    private static partial Regex ModelLine();

    [GeneratedRegex(@"^\s*description\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DescriptionLine();

    [GeneratedRegex(@"^\s*mode\s*:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex ModeLine();

    [GeneratedRegex(@"^\s*display_name\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex DisplayNameLine();

    /// <summary>Friendly name for an agent id: the id title-cased (e.g. <c>plan-architect</c> → <c>Plan Architect</c>), or <c>Main</c> when empty.</summary>
    public static string DisplayName(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return "Main";
        }

        var words = agentId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    /// <summary>Builds the catalog from the repository at <paramref name="repoRoot"/>.</summary>
    public static WorkflowCatalog Read(string repoRoot)
    {
        return new WorkflowCatalog(
            ReadBaseModel(Path.Combine(repoRoot, "opencode.json")),
            BaseWorkflow,
            ReadRoles(Path.Combine(repoRoot, ".opencode", "agents")));
    }

    /// <summary>Reads the <c>model</c> field from <c>opencode.json</c>.</summary>
    public static string ReadBaseModel(string opencodeJsonPath)
    {
        try
        {
            if (!File.Exists(opencodeJsonPath))
            {
                return FallbackModel;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(opencodeJsonPath));
            return document.RootElement.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.String
                && model.GetString() is { Length: > 0 } value
                ? value
                : FallbackModel;
        }
        catch (JsonException)
        {
            return FallbackModel;
        }
        catch (IOException)
        {
            return FallbackModel;
        }
    }

    /// <summary>Reads every <c>*.md</c> agent config in <paramref name="agentsDirectory"/>.</summary>
    public static IReadOnlyList<WorkflowRole> ReadRoles(string agentsDirectory)
    {
        if (!Directory.Exists(agentsDirectory))
        {
            return Array.Empty<WorkflowRole>();
        }

        var roles = new List<WorkflowRole>();
        foreach (var file in Directory.EnumerateFiles(agentsDirectory, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            roles.Add(ParseAgent(Path.GetFileNameWithoutExtension(file), content));
        }

        return roles;
    }

    /// <summary>Parses one agent markdown file into a role.</summary>
    public static WorkflowRole ParseAgent(string name, string content)
    {
        var model = ModelLine().Match(content) is { Success: true } modelMatch
            ? modelMatch.Groups[1].Value.Trim()
            : string.Empty;

        var description = DescriptionLine().Match(content) is { Success: true } descriptionMatch
            ? descriptionMatch.Groups[1].Value.Trim()
            : string.Empty;

        var mode = ModeLine().Match(content) is { Success: true } modeMatch
            ? modeMatch.Groups[1].Value.Trim()
            : string.Empty;

        var displayName = DisplayNameLine().Match(content) is { Success: true } displayMatch
            ? displayMatch.Groups[1].Value.Trim()
            : DisplayName(name);

        return new WorkflowRole(name, description, model, DeniesEdits(content), mode, displayName);
    }

    /// <summary>
    /// True when the front-matter grants an <c>edit</c> action only with <c>effect: deny</c>,
    /// i.e. the agent is read-only. Lines are scanned in order so an intervening
    /// <c>resource:</c> line does not hide the pairing.
    /// </summary>
    private static bool DeniesEdits(string content)
    {
        string? pendingAction = null;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                line = line[2..].Trim();
            }

            if (line.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
            {
                pendingAction = line["action:".Length..].Trim();
            }
            else if (line.StartsWith("effect:", StringComparison.OrdinalIgnoreCase))
            {
                var effect = line["effect:".Length..].Trim();
                if (string.Equals(pendingAction, "edit", StringComparison.OrdinalIgnoreCase)
                    && effect.Equals("deny", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                pendingAction = null;
            }
        }

        return false;
    }
}
