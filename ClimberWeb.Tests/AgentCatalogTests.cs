using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>Resolving a target folder's agents and the default one to run.</summary>
public class AgentCatalogTests
{
    [Fact]
    public void Resolve_PrefersTaskOrchestratorOverAnotherPrimary()
    {
        var folder = NewFolder();
        try
        {
            WriteAgent(folder, "other-primary", "mode: primary\nmodel: m");
            WriteAgent(folder, "task-orchestrator", "mode: primary\nmodel: m");
            WriteAgent(folder, "plan-architect", "mode: subagent\nmodel: m");

            var selection = AgentCatalog.Resolve(folder);

            Assert.Equal("task-orchestrator", selection.DefaultAgent);
            Assert.Equal(3, selection.Roles.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesConfiguredDefaultWhenPresent()
    {
        var folder = NewFolder();
        try
        {
            WriteAgent(folder, "task-orchestrator", "mode: primary\nmodel: m");
            WriteAgent(folder, "custom", "mode: primary\nmodel: m");

            Assert.Equal("custom", AgentCatalog.Resolve(folder, "custom").DefaultAgent);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_FallsBackToTheFirstPrimaryAgent()
    {
        var folder = NewFolder();
        try
        {
            WriteAgent(folder, "alpha", "mode: primary\nmodel: m");
            WriteAgent(folder, "beta", "mode: subagent\nmodel: m");

            Assert.Equal("alpha", AgentCatalog.Resolve(folder).DefaultAgent);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Resolve_NoAgents_YieldsNoDefault()
    {
        var folder = NewFolder();
        try
        {
            Assert.Null(AgentCatalog.Resolve(folder).DefaultAgent);

            var none = AgentCatalog.Resolve(null);
            Assert.Null(none.DefaultAgent);
            Assert.Empty(none.Roles);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string NewFolder() => Directory.CreateTempSubdirectory("climber-agents").FullName;

    private static void WriteAgent(string folder, string name, string frontMatter)
    {
        var directory = Path.Combine(folder, ".opencode", "agents");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".md"), frontMatter + "\n\nBody\n");
    }
}
