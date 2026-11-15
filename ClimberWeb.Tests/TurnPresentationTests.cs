using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class TurnPresentationTests
{
    private static LogEntry Tool(StepStatus status = StepStatus.None, LogKind kind = LogKind.Tool) =>
        new("raw", "12:00:00", kind, kind == LogKind.Subagent ? "plan-architect" : "shell", Status: status);

    private static LogEntry Reasoning() => new("raw", "12:00:00", LogKind.Reasoning, "thinking");

    private static LogEntry Footer() => new("raw", "12:00:00", LogKind.StepFinish, "stop · $0.0002 · 221 in / 116 out");

    [Fact]
    public void CompletedTurnSummarisesItsStepsAndCarriesTheFooter()
    {
        var view = TurnPresentation.For(
            [Reasoning(), Tool(), Tool(kind: LogKind.Subagent), Tool()],
            Footer());

        Assert.Equal("Worked in 3 steps", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.False(view.Open);
        Assert.Equal("stop · $0.0002 · 221 in / 116 out", view.Footer);
    }

    [Fact]
    public void ASingleStepIsSingular()
    {
        var view = TurnPresentation.For([Tool()], Footer());

        Assert.Equal("Worked in 1 step", view.Summary);
    }

    [Fact]
    public void AnOpenTurnReadsAsWorking()
    {
        var view = TurnPresentation.For([Reasoning(), Tool(StepStatus.Running)], footer: null, open: true, live: true);

        Assert.Equal("Working in 1 step", view.Summary);
        Assert.Equal(StepStatus.Running, view.Status);
        Assert.True(view.Open);
        Assert.Null(view.Footer);
    }

    [Fact]
    public void AnOpenTurnWhoseRunStoppedSettlesInsteadOfWorking()
    {
        var view = TurnPresentation.For([Reasoning(), Tool(), Tool()], footer: null, open: true, live: false);

        Assert.Equal("Stopped after 2 steps", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.False(view.Open);
    }

    [Fact]
    public void AStoppedNewestTurnOpensButDoesNotShimmer()
    {
        var view = TurnPresentation.For([Tool(), Tool()], footer: null, open: true, live: false, newest: true);

        Assert.Equal("Stopped after 2 steps", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void TheNewestTurnStaysOpenAfterItsFooterArrives()
    {
        var view = TurnPresentation.For([Tool(), Tool()], Footer(), newest: true);

        Assert.Equal("Worked in 2 steps", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void ALiveNewestTurnShimmersEvenAfterItsFooterArrives()
    {
        var view = TurnPresentation.For(
            [Tool(), Tool()],
            Footer(),
            live: true,
            newest: true,
            active: true);

        Assert.Equal("Working in 2 steps", view.Summary);
        Assert.Equal(StepStatus.Running, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void ALiveTurnBehindNewerProseOpensWithoutClaimingToWork()
    {
        var view = TurnPresentation.For(
            [Tool(), Tool()],
            Footer(),
            live: true,
            newest: true,
            active: false);

        Assert.Equal("Worked in 2 steps", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void AnOlderTurnKeepsTheAllCollapsedDefault()
    {
        var view = TurnPresentation.For([Tool()], Footer(), live: true);

        Assert.Equal("Worked in 1 step", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.False(view.Open);
    }

    [Fact]
    public void AnOpenThinkingTurnWhoseRunStoppedSettles()
    {
        var view = TurnPresentation.For([Reasoning()], footer: null, open: true, live: false);

        Assert.Equal("Stopped while thinking", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.False(view.Open);
    }

    [Fact]
    public void AStoppedTurnStillMarksFailures()
    {
        var view = TurnPresentation.For([Tool(), Tool(StepStatus.Failed)], footer: null, open: true, live: false);

        Assert.Equal("Stopped after 2 steps · 1 failed", view.Summary);
        Assert.Equal(StepStatus.Failed, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void ATurnClosedWithoutAFooterIsNotStillRunning()
    {
        var view = TurnPresentation.For([Tool()], footer: null, open: false);

        Assert.Equal("Worked in 1 step", view.Summary);
        Assert.Equal(StepStatus.None, view.Status);
        Assert.False(view.Open);
    }

    [Fact]
    public void AFailedRowMarksTheTurnAndOpensIt()
    {
        var view = TurnPresentation.For([Tool(), Tool(StepStatus.Failed)], Footer());

        Assert.Equal("Worked in 2 steps · 1 failed", view.Summary);
        Assert.Equal(StepStatus.Failed, view.Status);
        Assert.True(view.Open);
    }

    [Fact]
    public void AReasoningOnlyTurnSaysThought()
    {
        var completed = TurnPresentation.For([Reasoning()], Footer());
        var running = TurnPresentation.For([Reasoning()], footer: null, open: true, live: true);

        Assert.Equal("Thought", completed.Summary);
        Assert.Equal("Thinking…", running.Summary);
    }

    [Fact]
    public void ARunningRowSettlesOnlyOnceItsRunStops()
    {
        var running = Tool(StepStatus.Running);

        var live = TurnPresentation.Settled([running], live: true);
        var settled = TurnPresentation.Settled([running], live: false);

        Assert.Same(running, live[0]);
        Assert.Equal(StepStatus.None, settled[0].Status);
    }

    [Fact]
    public void SettlingLeavesFinishedRowsExactlyAsTheyWere()
    {
        var done = Tool();
        var failed = Tool(StepStatus.Failed);

        var settled = TurnPresentation.Settled([done, failed], live: false);

        Assert.Same(done, settled[0]);
        Assert.Same(failed, settled[1]);
    }
}