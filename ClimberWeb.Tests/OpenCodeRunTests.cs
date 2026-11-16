using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>Building the opencode invocation, with and without an explicit agent.</summary>
public class OpenCodeRunTests
{
    [Fact]
    public void Request_WithoutAgent_OmitsTheFlag()
    {
        var request = OpenCodeRun.Request("do it", @"C:\repo");

        Assert.Equal("opencode", request.FileName);
        Assert.Equal(new[] { "run", "do it", "--format", "json" }, request.Arguments);
    }

    [Fact]
    public void Request_WithAgent_AddsTheAgentFlag()
    {
        var request = OpenCodeRun.Request("do it", @"C:\repo", "task-orchestrator");

        Assert.Equal(new[] { "run", "do it", "--agent", "task-orchestrator", "--format", "json" }, request.Arguments);
        Assert.Equal(@"C:\repo", request.WorkingDirectory);
    }

    [Fact]
    public void CommandLine_ShowsTheAgentWhenSet()
    {
        Assert.Equal("opencode run \"do it\" --format json", OpenCodeRun.CommandLine("do it"));
        Assert.Equal(
            "opencode run \"do it\" --agent task-orchestrator --format json",
            OpenCodeRun.CommandLine("do it", "task-orchestrator"));
    }

    [Fact]
    public void Request_WithModel_AddsTheModelFlagAfterTheAgent()
    {
        var request = OpenCodeRun.Request("do it", @"C:\repo", "task-orchestrator", "surplus/deepseek-v4.1-flash-1017");

        Assert.Equal(
            new[] { "run", "do it", "--agent", "task-orchestrator", "--model", "surplus/deepseek-v4.1-flash-1017", "--format", "json" },
            request.Arguments);
    }

    [Fact]
    public void CommandLine_ShowsTheModelWhenSet()
    {
        Assert.Equal(
            "opencode run \"do it\" --agent task-orchestrator --model surplus/deepseek-v4.1-flash-1017 --format json",
            OpenCodeRun.CommandLine("do it", "task-orchestrator", "surplus/deepseek-v4.1-flash-1017"));
    }

    [Fact]
    public void Request_WithSession_ContinuesThatConversation()
    {
        var request = OpenCodeRun.Request("do it", @"C:\repo", sessionId: "ses_abc123");

        Assert.Equal(
            new[] { "run", "do it", "--session", "ses_abc123", "--format", "json" },
            request.Arguments);
    }

    [Fact]
    public void CommandLine_ShowsTheSessionWhenSet()
    {
        Assert.Equal(
            "opencode run \"do it\" --session ses_abc123 --format json",
            OpenCodeRun.CommandLine("do it", sessionId: "ses_abc123"));
    }

    [Fact]
    public void Request_WithoutSession_LetsOpencodeMintItsOwn()
    {
        var request = OpenCodeRun.Request("do it", @"C:\repo", sessionId: null);

        Assert.DoesNotContain("--session", request.Arguments);
    }
}
