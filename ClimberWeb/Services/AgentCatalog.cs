namespace ClimberWeb.Services;

/// <summary>The agents a target folder offers and the one a run will use by default.</summary>
/// <param name="Roles">Agents found for the target (its own, else the global config), name-ordered.</param>
/// <param name="DefaultAgent">
/// The name passed to <c>opencode run --agent</c>, or null to let opencode use its own default.
/// </param>
/// <param name="FromGlobal">
/// True when the agents came from the global opencode config rather than the target folder.
/// </param>
public sealed record AgentSelection(IReadOnlyList<WorkflowRole> Roles, string? DefaultAgent, bool FromGlobal = false);

/// <summary>
/// Resolves the agents to run with, so a general run and a hillclimb use the same
/// project-owned agent rather than opencode's bare default. The target folder's own agents
/// win; when it ships none, the global opencode config is used — which is exactly what
/// opencode itself consults, so a name resolved here is a name <c>--agent</c> accepts.
/// </summary>
public static class AgentCatalog
{
    /// <summary>The primary agent the dashboard prefers when the target ships one.</summary>
    public const string PreferredAgent = "task-orchestrator";

    /// <summary>Names the arrangement of the orchestrator and its read-only delegates.</summary>
    public const string WorkflowName = "orchestrated";

    /// <summary>
    /// Reads <paramref name="folder"/>'s <c>.opencode/agents</c> and picks a default: the
    /// configured name when present, else <see cref="PreferredAgent"/>, else the first primary
    /// agent, else the same from <paramref name="globalAgentsDirectory"/>. A missing folder or
    /// agent directory yields no agents.
    /// </summary>
    public static AgentSelection Resolve(string? folder, string? configuredDefault = null, string? globalAgentsDirectory = null)
    {
        var roles = ReadTargetRoles(folder);
        var fromGlobal = false;

        if (roles.Count == 0 && !string.IsNullOrWhiteSpace(globalAgentsDirectory))
        {
            var global = WorkflowCatalogReader.ReadRoles(globalAgentsDirectory);
            if (global.Count > 0)
            {
                roles = global;
                fromGlobal = true;
            }
        }

        return new AgentSelection(roles, PickDefault(roles, configuredDefault), fromGlobal);
    }

    /// <summary>The global opencode agents directory (<c>~/.config/opencode/agents</c>).</summary>
    public static string GlobalAgentsDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "opencode",
            "agents");

    private static IReadOnlyList<WorkflowRole> ReadTargetRoles(string? folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? Array.Empty<WorkflowRole>()
            : WorkflowCatalogReader.ReadRoles(Path.Combine(folder, ".opencode", "agents"));

    private static string? PickDefault(IReadOnlyList<WorkflowRole> roles, string? configuredDefault)
    {
        if (roles.Count == 0)
        {
            return null;
        }

        return Find(roles, configuredDefault)
            ?? Find(roles, PreferredAgent)
            ?? roles.FirstOrDefault(role => role.IsPrimary)?.Name;
    }

    private static string? Find(IReadOnlyList<WorkflowRole> roles, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : roles.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
}
