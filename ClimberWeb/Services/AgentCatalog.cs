namespace ClimberWeb.Services;

/// <summary>The agents a target folder offers and the one a run will use by default.</summary>
/// <param name="Roles">Agents found under the target's <c>.opencode/agents</c>, name-ordered (may be empty).</param>
/// <param name="DefaultAgent">
/// The name passed to <c>opencode run --agent</c>, or null to let opencode use its own default.
/// </param>
public sealed record AgentSelection(IReadOnlyList<WorkflowRole> Roles, string? DefaultAgent);

/// <summary>
/// Resolves the agents a target folder offers and the default one to run, so a general run
/// and a hillclimb use the same project-owned agent rather than opencode's bare default.
/// </summary>
public static class AgentCatalog
{
    /// <summary>The primary agent the dashboard prefers when the target ships one.</summary>
    public const string PreferredAgent = "task-orchestrator";

    /// <summary>
    /// Reads the target folder's <c>.opencode/agents</c> and picks a default: the
    /// configured name when it is present, else <see cref="PreferredAgent"/>, else the first
    /// primary agent, else none. A missing folder or agent directory yields no agents.
    /// </summary>
    public static AgentSelection Resolve(string? folder, string? configuredDefault = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new AgentSelection(Array.Empty<WorkflowRole>(), null);
        }

        var roles = WorkflowCatalogReader.ReadRoles(Path.Combine(folder, ".opencode", "agents"));
        return new AgentSelection(roles, PickDefault(roles, configuredDefault));
    }

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
