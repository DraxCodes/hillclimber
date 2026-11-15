using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class OutputActivityTests
{
    private static LogEntry Entry(
        LogKind kind,
        string text,
        StepStatus status = StepStatus.None,
        string? input = null) =>
        new($"raw-{kind}-{text}", "12:00:00", kind, text, ToolInput: input, Status: status);

    [Fact]
    public void AStoppedRunIsIdle()
    {
        var activity = OutputActivity.For([Entry(LogKind.Tool, "shell")], live: false);

        Assert.False(activity.Working);
        Assert.Equal("idle", activity.Label);
    }

    [Fact]
    public void ALiveRunWithNoRowsIsStarting()
    {
        var activity = OutputActivity.For(Array.Empty<LogEntry>(), live: true);

        Assert.True(activity.Working);
        Assert.Equal("starting", activity.Label);
    }

    [Fact]
    public void ARunningToolNamesTheCallWithoutRepeatingItsState()
    {
        var activity = OutputActivity.For(
            [Entry(LogKind.Tool, "shell", StepStatus.Running, input: "command=dotnet test")],
            live: true);

        Assert.Equal("dotnet test", activity.Label);
    }

    [Fact]
    public void ARunningToolKeepsOpencodesOwnDescription()
    {
        var activity = OutputActivity.For(
            [
                new LogEntry(
                    "raw",
                    "12:00:00",
                    LogKind.Tool,
                    "shell",
                    ToolInput: "command=git status · description=Check the working tree",
                    ToolTitle: "Check the working tree",
                    Status: StepStatus.Running),
            ],
            live: true);

        Assert.Equal("Check the working tree", activity.Label);
    }

    [Fact]
    public void ARunningSubagentSpendsItsWidthOnTheActNotTheName()
    {
        var activity = OutputActivity.For(
            [Entry(LogKind.Subagent, "plan-architect", StepStatus.Running)],
            live: true);

        Assert.Equal("delegating", activity.Label);
    }

    [Fact]
    public void ReasoningReadsAsThinkingAndTextAsWriting()
    {
        Assert.Equal("thinking", OutputActivity.For([Entry(LogKind.Reasoning, "hmm")], live: true).Label);
        Assert.Equal("writing", OutputActivity.For([Entry(LogKind.Text, "answer")], live: true).Label);
    }

    [Theory]
    [InlineData(LogKind.StepFinish)]
    [InlineData(LogKind.StepStart)]
    [InlineData(LogKind.Tool)]
    public void AQuietBoundaryFallsBackToWorking(LogKind kind)
    {
        var activity = OutputActivity.For([Entry(kind, "done")], live: true);

        Assert.True(activity.Working);
        Assert.Equal("working", activity.Label);
    }

    [Fact]
    public void TheNewestDelegateTabIsWorking()
    {
        var entries = new[]
        {
            Entry(LogKind.Tool, "shell"),
            Entry(LogKind.Subagent, "plan-architect", StepStatus.Running),
        };

        Assert.True(OutputActivity.TabWorking("plan-architect", entries, live: true));
    }

    [Fact]
    public void AnOlderDelegateTabIsNotWorking()
    {
        var entries = new[]
        {
            Entry(LogKind.Subagent, "plan-architect", StepStatus.Running),
            Entry(LogKind.Tool, "shell"),
        };

        Assert.False(OutputActivity.TabWorking("plan-architect", entries, live: true));
    }

    [Fact]
    public void AFailedDelegateTabDoesNotPulse()
    {
        var entries = new[] { Entry(LogKind.Subagent, "quality-gatekeeper", StepStatus.Failed) };

        Assert.False(OutputActivity.TabWorking("quality-gatekeeper", entries, live: true));
    }

    [Fact]
    public void AStoppedRunHasNoWorkingTab()
    {
        var entries = new[] { Entry(LogKind.Subagent, "plan-architect", StepStatus.Running) };

        Assert.False(OutputActivity.TabWorking("plan-architect", entries, live: false));
    }

    [Fact]
    public void ADifferentAgentsPointerDoesNotPulseThisTab()
    {
        var entries = new[] { Entry(LogKind.Subagent, "security-auditor", StepStatus.Running) };

        Assert.False(OutputActivity.TabWorking("plan-architect", entries, live: true));
    }
}