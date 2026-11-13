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
            WriteTargetAgent(folder, "other-primary", "mode: primary\nmodel: m");
            WriteTargetAgent(folder, "task-orchestrator", "mode: primary\nmodel: m");
            WriteTargetAgent(folder, "plan-architect", "mode: subagent\nmodel: m");

            var selection = AgentCatalog.Resolve(folder);

            Assert.Equal("task-orchestrator", selection.DefaultAgent);
            Assert.Equal(3, selection.Roles.Count);
            Assert.False(selection.FromGlobal);
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
            WriteTargetAgent(folder, "task-orchestrator", "mode: primary\nmodel: m");
            WriteTargetAgent(folder, "custom", "mode: primary\nmodel: m");

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
            WriteTargetAgent(folder, "alpha", "mode: primary\nmodel: m");
            WriteTargetAgent(folder, "beta", "mode: subagent\nmodel: m");

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

    [Fact]
    public void Resolve_FallsBackToTheGlobalAgents_WhenTheTargetHasNoAgents()
    {
        var target = NewFolder();
        var global = NewFolder();
        try
        {
            WriteAgentsFile(Path.Combine(global, "task-orchestrator.md"), "mode: primary\nmodel: m");
            WriteAgentsFile(Path.Combine(global, "plan-architect.md"), "mode: subagent\nmodel: m");

            var selection = AgentCatalog.Resolve(target, globalAgentsDirectory: global);

            Assert.Equal("task-orchestrator", selection.DefaultAgent);
            Assert.Equal(2, selection.Roles.Count);
            Assert.True(selection.FromGlobal);
        }
        finally
        {
            Directory.Delete(target, recursive: true);
            Directory.Delete(global, recursive: true);
        }
    }

    [Fact]
    public void Resolve_TargetAgentsWinOverTheGlobalOnes()
    {
        var target = NewFolder();
        var global = NewFolder();
        try
        {
            WriteTargetAgent(target, "my-agent", "mode: primary\nmodel: m");
            WriteAgentsFile(Path.Combine(global, "task-orchestrator.md"), "mode: primary\nmodel: m");

            var selection = AgentCatalog.Resolve(target, globalAgentsDirectory: global);

            Assert.Equal("my-agent", selection.DefaultAgent);
            Assert.Single(selection.Roles);
            Assert.False(selection.FromGlobal);
        }
        finally
        {
            Directory.Delete(target, recursive: true);
            Directory.Delete(global, recursive: true);
        }
    }

    private static string NewFolder() => Directory.CreateTempSubdirectory("climber-agents").FullName;

    private static void WriteTargetAgent(string folder, string name, string frontMatter) =>
        WriteAgentsFile(Path.Combine(folder, ".opencode", "agents", name + ".md"), frontMatter);

    private static void WriteAgentsFile(string path, string frontMatter)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, frontMatter + "\n\nBody\n");
    }
}
