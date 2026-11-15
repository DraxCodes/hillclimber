namespace ClimberWeb.Services;

/// <summary>Which way the transcript's scroll offset grows.</summary>
public enum ScrollAxis
{
    /// <summary>The combined feed is <c>column-reverse</c>: the live edge is 0 and older rows are negative.</summary>
    Reverse,

    /// <summary>A solo subagent tab reads top-down: 0 is the top and older rows are positive.</summary>
    Forward,
}

/// <summary>The geometry a disclosure toggle sees, in viewport and feed coordinates.</summary>
/// <param name="BeforeTop">The summary's viewport top before the toggle.</param>
/// <param name="AfterTop">The summary's viewport top after the toggle.</param>
/// <param name="BodyTop">The body's viewport top after the toggle; 0 when closed.</param>
/// <param name="BodyBottom">The body's viewport bottom after the toggle; 0 when closed.</param>
/// <param name="FeedTop">The feed's viewport top.</param>
/// <param name="FeedBottom">The feed's viewport bottom.</param>
/// <param name="ScrollTop">The feed's current scroll offset.</param>
/// <param name="ScrollHeight">The feed's scroll height.</param>
/// <param name="ClientHeight">The feed's visible height.</param>
public readonly record struct ScrollMetrics(
    double BeforeTop,
    double AfterTop,
    double BodyTop,
    double BodyBottom,
    double FeedTop,
    double FeedBottom,
    double ScrollTop,
    double ScrollHeight,
    double ClientHeight);

/// <summary>
/// The arithmetic behind keeping a disclosure under the reader's eye. Pure so the script in
/// <c>App.razor</c> and the tests agree on every clamp; the DOM wiring measures the summary
/// before and after the native toggle and asks this for the offset to apply.
/// </summary>
public static class StepScrollMath
{
    /// <summary>Below this many pixels the movement is not worth an animation.</summary>
    public const double NoOpThreshold = 2;

    /// <summary>A body taller than the panel minus this margin is anchored to the near edge.</summary>
    public const double TallBlockMargin = 60;

    /// <summary>
    /// Returns the scroll offset that keeps the summary where it was on open or close, or
    /// aligns a block taller than the panel to the feed's near edge. Returns null when the
    /// summary is already where it belongs or the movement is below the no-op threshold.
    /// </summary>
    /// <param name="axis">Whether the feed grows up (combined) or down (solo).</param>
    /// <param name="open">The disclosure's state after the toggle.</param>
    /// <param name="bodyHeight">The body's rendered height after the toggle; 0 when closed.</param>
    /// <param name="metrics">The measured geometry around the toggle.</param>
    public static double? Target(ScrollAxis axis, bool open, double bodyHeight, ScrollMetrics metrics)
    {
        var feedHeight = metrics.FeedBottom - metrics.FeedTop;
        var tall = open && bodyHeight > feedHeight - TallBlockMargin;

        // The summary moved by this much between the two measurements. Scrolling by the same
        // amount moves the content back: a larger offset always moves rows up the viewport.
        var delta = tall ? metrics.AfterTop - metrics.FeedTop : metrics.AfterTop - metrics.BeforeTop;

        // A body that fits the panel but not the space left below its summary would still
        // open out of view. Nudge it until the body sits inside the feed, then keep the
        // summary itself inside as a final guard.
        if (open && !tall && bodyHeight > 0 && metrics.BodyBottom > 0)
        {
            var bodyBottomAfter = metrics.BodyBottom - delta;
            if (bodyBottomAfter > metrics.FeedBottom + NoOpThreshold)
            {
                delta += bodyBottomAfter - metrics.FeedBottom;
            }

            var bodyTopAfter = metrics.BodyTop - delta;
            if (bodyTopAfter < metrics.FeedTop - NoOpThreshold)
            {
                delta -= metrics.FeedTop - bodyTopAfter;
            }
        }

        var summaryAfter = metrics.AfterTop - delta;
        if (summaryAfter < metrics.FeedTop - NoOpThreshold)
        {
            delta -= metrics.FeedTop - summaryAfter;
        }
        else if (summaryAfter > metrics.FeedBottom + NoOpThreshold)
        {
            delta += summaryAfter - metrics.FeedBottom;
        }

        if (Math.Abs(delta) < NoOpThreshold)
        {
            return null;
        }

        var max = Math.Max(0, metrics.ScrollHeight - metrics.ClientHeight);
        var target = metrics.ScrollTop + delta;
        target = axis == ScrollAxis.Reverse
            ? Math.Clamp(target, -max, 0)
            : Math.Clamp(target, 0, max);

        return Math.Abs(target - metrics.ScrollTop) < 0.5 ? null : target;
    }
}