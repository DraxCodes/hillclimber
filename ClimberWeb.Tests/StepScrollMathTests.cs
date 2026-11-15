using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class StepScrollMathTests
{
    private static ScrollMetrics Metrics(
        double before = 400,
        double after = 400,
        double bodyTop = 0,
        double bodyBottom = 0,
        double feedTop = 100,
        double feedBottom = 620,
        double scrollTop = 0,
        double scrollHeight = 2000,
        double clientHeight = 520) =>
        new(before, after, bodyTop, bodyBottom, feedTop, feedBottom, scrollTop, scrollHeight, clientHeight);

    [Fact]
    public void AMovedSummaryIsPinnedInTheCombinedFeed()
    {
        // Baseline evidence: the summary moved from 871 to 745 and the browser left the
        // feed at -187; restoring it needs another -126.
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 126,
            Metrics(before: 871, after: 745, feedTop: 783, feedBottom: 1303, scrollTop: -187));

        Assert.Equal(-313, target);
    }

    [Fact]
    public void AMovedSummaryIsPinnedOnASoloTab()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Forward, open: true, bodyHeight: 80,
            Metrics(before: 200, after: 260, scrollTop: 40, scrollHeight: 900));

        Assert.Equal(100, target);
    }

    [Fact]
    public void AClosingDisclosurePinsItsSummary()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: false, bodyHeight: 0,
            Metrics(before: 500, after: 620, scrollTop: -400));

        Assert.Equal(-280, target);
    }

    [Fact]
    public void ABlockTallerThanThePanelAlignsToTheNearEdge()
    {
        // Body 600 > feed 520 - 60, so the summary's after-top decides, not the delta.
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 600,
            Metrics(before: 900, after: 646, feedTop: 783, feedBottom: 1303, scrollTop: -187));

        Assert.Equal(-324, target);
    }

    [Fact]
    public void AShortBlockInsideThePanelKeepsThePinEvenWhenTheBodyFits()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Forward, open: true, bodyHeight: 120,
            Metrics(before: 300, after: 310, scrollTop: 0, scrollHeight: 2000));

        Assert.Equal(10, target);
    }

    [Fact]
    public void ReverseScrollClampsAtTheLiveEdge()
    {
        // Moving content down past the live edge is impossible, so the target stops at 0.
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 40,
            Metrics(before: 100, after: 160, scrollTop: -10));

        Assert.Equal(0, target);
    }

    [Fact]
    public void ForwardScrollClampsAtTheOldestRow()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Forward, open: true, bodyHeight: 40,
            Metrics(before: 100, after: 160, scrollTop: 340, scrollHeight: 900, clientHeight: 520));

        Assert.Equal(380, target);
    }

    [Fact]
    public void ABodyThatFitsThePanelButNotTheSpaceBelowIsNudgedIntoView()
    {
        // Baseline: a 359px body opened with its summary pinned at 1005 still ran 90px past
        // the feed's bottom (1303). The extra nudge moves the content up just enough.
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 359,
            Metrics(before: 1005, after: 1005, bodyTop: 1034, bodyBottom: 1393,
                feedTop: 783, feedBottom: 1303, scrollTop: -546));

        Assert.Equal(-456, target);
    }

    [Fact]
    public void ALongTerminalBodyIsNudgedByOnlyItsOverflow()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 230,
            Metrics(before: 1073, after: 1073, bodyTop: 1125, bodyBottom: 1355,
                feedTop: 783, feedBottom: 1303, scrollTop: -776));

        Assert.Equal(-724, target);
    }

    [Fact]
    public void ASmallMovementIsNotWorthAnAnimation()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: true, bodyHeight: 40,
            Metrics(before: 400, after: 401));

        Assert.Null(target);
    }

    [Fact]
    public void AnUnmovedSummaryNeedsNoAdjustment()
    {
        var target = StepScrollMath.Target(
            ScrollAxis.Forward, open: false, bodyHeight: 0,
            Metrics(before: 400, after: 400, scrollTop: 200));

        Assert.Null(target);
    }

    [Fact]
    public void TallThresholdUsesTheOpenState()
    {
        // A tall body that is closed again must pin, not align to the near edge.
        var target = StepScrollMath.Target(
            ScrollAxis.Reverse, open: false, bodyHeight: 600,
            Metrics(before: 500, after: 620, scrollTop: -400));

        Assert.Equal(-280, target);
    }
}