using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class ToolPresentationTests
{
    private static LogEntry Tool(
        string name,
        string? input = null,
        string? output = null,
        StepStatus status = StepStatus.None,
        string? title = null,
        string? command = null,
        int? exitCode = null) =>
        new("raw", "12:00:00", LogKind.Tool, name, input, output, status, title, command, exitCode);

    private static LogEntry Subagent(
        string agent,
        string? prompt = null,
        string? output = null,
        StepStatus status = StepStatus.None) =>
        new("raw", "12:00:00", LogKind.Subagent, agent, prompt, output, status);

    [Fact]
    public void ShellUsesTheCommandAsTheTitleAndTheOutputAsDetail()
    {
        var view = ToolPresentation.For(Tool("shell", "command=dotnet test", "269 passed"), primaryTab: true);

        Assert.Equal("terminal", view.Icon);
        Assert.Equal("Ran dotnet test", view.Title);
        Assert.Null(view.Description);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.Equal("269 passed", view.Detail);
    }

    [Fact]
    public void FailedShellUsesTheFailedVerb()
    {
        var view = ToolPresentation.For(
            Tool("shell", "command=dotnet build", "error CS1002", StepStatus.Failed),
            primaryTab: true);

        Assert.Equal("Failed to run dotnet build", view.Title);
        Assert.Equal(StepStatus.Failed, view.Status);
        Assert.Equal("error CS1002", view.Detail);
    }

    [Fact]
    public void FailedReadKeepsItsOwnVerb()
    {
        var view = ToolPresentation.For(
            Tool("read", "filePath=src/Foo.cs", "denied", StepStatus.Failed),
            primaryTab: true);

        Assert.Equal("Failed to read src/Foo.cs", view.Title);
        Assert.Equal(StepStatus.Failed, view.Status);
    }

    [Fact]
    public void FailedEditKeepsItsOwnVerb()
    {
        var view = ToolPresentation.For(
            Tool("edit", "filePath=src/Foo.cs", "denied", StepStatus.Failed),
            primaryTab: true);

        Assert.Equal("Failed to edit src/Foo.cs", view.Title);
    }

    [Fact]
    public void RunningShellUsesTheRunningVerb()
    {
        var view = ToolPresentation.For(
            Tool("shell", "command=dotnet test", status: StepStatus.Running),
            primaryTab: true);

        Assert.Equal("Running dotnet test", view.Title);
        Assert.Equal(StepStatus.Running, view.Status);
        Assert.Null(view.Detail);
    }

    [Fact]
    public void RunningReadUsesTheProgressiveVerb()
    {
        var view = ToolPresentation.For(
            Tool("read", "filePath=src/Foo.cs", status: StepStatus.Running),
            primaryTab: true);

        Assert.Equal("Reading src/Foo.cs", view.Title);
    }

    [Fact]
    public void ReadUsesThePathAndKeepsExtraInputAsDescription()
    {
        var view = ToolPresentation.For(
            Tool("read", "filePath=src/Foo.cs · limit=20", "contents"),
            primaryTab: true);

        Assert.Equal("file", view.Icon);
        Assert.Equal("Read src/Foo.cs", view.Title);
        Assert.Equal("limit=20", view.Description);
    }

    [Fact]
    public void EditUsesThePath()
    {
        var view = ToolPresentation.For(Tool("edit", "filePath=src/Foo.cs", "applied"), primaryTab: true);

        Assert.Equal("pencil", view.Icon);
        Assert.Equal("Edited src/Foo.cs", view.Title);
        Assert.Null(view.Description);
    }

    [Fact]
    public void SearchUsesThePattern()
    {
        var view = ToolPresentation.For(Tool("grep", "pattern=TODO · path=src", "2 matches"), primaryTab: true);

        Assert.Equal("search", view.Icon);
        Assert.Equal("Searched TODO", view.Title);
        Assert.Equal("path=src", view.Description);
    }

    [Fact]
    public void WebFetchUsesTheUrl()
    {
        var view = ToolPresentation.For(
            Tool("webfetch", "url=https://example.com", "page"),
            primaryTab: true);

        Assert.Equal("globe", view.Icon);
        Assert.Equal("Fetched https://example.com", view.Title);
    }

    [Fact]
    public void UnknownToolKeepsTheWholeSummaryAsDescription()
    {
        var view = ToolPresentation.For(Tool("mystery", "foo=bar · baz=qux", "done"), primaryTab: true);

        Assert.Equal("gear", view.Icon);
        Assert.Equal("Ran mystery", view.Title);
        Assert.Equal("foo=bar · baz=qux", view.Description);
    }

    [Fact]
    public void UnknownToolWithoutInputHasNoDescription()
    {
        var view = ToolPresentation.For(Tool("mystery"), primaryTab: true);

        Assert.Equal("Ran mystery", view.Title);
        Assert.Null(view.Description);
    }

    [Theory]
    [InlineData("session.updated", "session updated")]
    [InlineData("mcp_tools-changed", "mcp tools changed")]
    public void UnknownEventReadsAsLowercaseProse(string type, string title)
    {
        var view = ToolPresentation.ForEvent(Tool(type));

        Assert.Equal("gear", view.Icon);
        Assert.Equal(title, view.Title);
        Assert.Null(view.Description);
        Assert.Equal("raw", view.Detail);
    }

    [Fact]
    public void KnownToolWithoutItsKeyFallsBackToTheToolName()
    {
        var view = ToolPresentation.For(Tool("read", "limit=20"), primaryTab: true);

        Assert.Equal("Ran read", view.Title);
        Assert.Equal("limit=20", view.Description);
    }

    [Fact]
    public void ToolWithoutOutputHasNoDetail()
    {
        var view = ToolPresentation.For(Tool("shell", "command=dotnet test"), primaryTab: true);

        Assert.Null(view.Detail);
    }

    [Fact]
    public void SubagentOnThePrimaryTabPointsAtItsOwnTab()
    {
        var view = ToolPresentation.For(
            Subagent("plan-architect", "Produce a plan.", "1. Do it."),
            primaryTab: true);

        Assert.Equal("subagent", view.Icon);
        Assert.Equal("plan-architect — see its tab", view.Title);
        Assert.Null(view.Description);
        Assert.Null(view.Detail);
    }

    [Fact]
    public void SubagentOnItsTabShowsThePromptAndResult()
    {
        var view = ToolPresentation.For(
            Subagent("plan-architect", "Produce a plan.", "1. Do it."),
            primaryTab: false);

        Assert.Equal("subagent", view.Icon);
        Assert.Equal("plan-architect subagent", view.Title);
        Assert.Equal("Produce a plan.", view.Description);
        Assert.Equal("1. Do it.", view.Detail);
    }

    [Fact]
    public void FailedSubagentKeepsTheFailedStatus()
    {
        var view = ToolPresentation.For(
            Subagent("plan-architect", "Produce a plan.", "boom", StepStatus.Failed),
            primaryTab: false);

        Assert.Equal(StepStatus.Failed, view.Status);
        Assert.Equal("Failed: plan-architect subagent", view.Title);
        Assert.Equal("boom", view.Detail);
    }

    [Fact]
    public void FailedSubagentOnThePrimaryTabSaysFailed()
    {
        var view = ToolPresentation.For(
            Subagent("plan-architect", "Produce a plan.", "boom", StepStatus.Failed),
            primaryTab: true);

        Assert.Equal("Failed: plan-architect — see its tab", view.Title);
        Assert.Equal(StepStatus.Failed, view.Status);
        Assert.Null(view.Detail);
    }

    [Fact]
    public void RunningSubagentOnThePrimaryTabSaysRunning()
    {
        var view = ToolPresentation.For(
            Subagent("plan-architect", "Produce a plan.", status: StepStatus.Running),
            primaryTab: true);

        Assert.Equal("Running: plan-architect — working on its tab", view.Title);
        Assert.Equal(StepStatus.Running, view.Status);
    }

    [Fact]
    public void LongTitleIsTruncatedToEightyCharacters()
    {
        var command = new string('x', 200);
        var view = ToolPresentation.For(Tool("shell", $"command={command}"), primaryTab: true);

        Assert.True(view.Title.Length <= 80, $"title was {view.Title.Length} characters");
        Assert.EndsWith("…", view.Title);
    }

    [Fact]
    public void HumanTitleWinsOverTheGrammarTitle()
    {
        var view = ToolPresentation.For(
            Tool("shell", "command=dotnet test", "269 passed", title: "Run the test suite", command: "dotnet test"),
            primaryTab: true);

        Assert.Equal("Run the test suite", view.Title);
        Assert.Equal("dotnet test", view.Command);
        Assert.Equal("269 passed", view.Detail);
    }

    [Fact]
    public void HumanTitleThatRepeatsThePathKeepsTheGrammarVerb()
    {
        var view = ToolPresentation.For(
            Tool("read", "filePath=src/Foo.cs", "contents", title: "src/Foo.cs"),
            primaryTab: true);

        Assert.Equal("Read src/Foo.cs", view.Title);
    }

    [Fact]
    public void HumanTitleEqualToTheCommandKeepsTheGrammarTitle()
    {
        var view = ToolPresentation.For(
            Tool("shell", "command=dotnet test", "ok", title: "dotnet test", command: "dotnet test"),
            primaryTab: true);

        Assert.Equal("Ran dotnet test", view.Title);
    }

    [Fact]
    public void FailedHumanTitleKeepsTheStatePrefix()
    {
        var view = ToolPresentation.For(
            Tool(
                "shell",
                "command=dotnet build",
                "error CS1002",
                StepStatus.Failed,
                title: "Build the solution",
                command: "dotnet build",
                exitCode: 1),
            primaryTab: true);

        Assert.Equal("Failed: Build the solution", view.Title);
        Assert.Equal(1, view.ExitCode);
        Assert.Equal(StepStatus.Failed, view.Status);
    }

    [Fact]
    public void ShellWithACommandExposesTheTerminalFields()
    {
        var view = ToolPresentation.For(
            Tool("shell", "command=dotnet test", "269 passed", command: "dotnet test", exitCode: 0),
            primaryTab: true);

        Assert.Equal("terminal", view.Icon);
        Assert.Equal("dotnet test", view.Command);
        Assert.Equal(0, view.ExitCode);
    }

    [Fact]
    public void ShellWithoutACommandKeepsTheTraceRow()
    {
        var view = ToolPresentation.For(Tool("shell", "command=dotnet test"), primaryTab: true);

        Assert.Null(view.Command);
        Assert.Null(view.ExitCode);
    }

    [Fact]
    public void NonShellToolNeverExposesATerminalCommand()
    {
        var view = ToolPresentation.For(Tool("read", "filePath=src/Foo.cs"), primaryTab: true);

        Assert.Null(view.Command);
    }
}