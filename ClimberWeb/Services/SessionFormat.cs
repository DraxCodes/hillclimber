using System.Globalization;

namespace ClimberWeb.Services;

/// <summary>
/// Formatting shared by the sessions rail, kept pure so the rail's copy is testable
/// without a browser. The summary text is deliberately terse: a rail row is one line
/// beside a title, not a timestamp column.
/// </summary>
public static class SessionFormat
{
    /// <summary>
    /// How long ago <paramref name="when"/> was, at rail width: <c>now</c>, <c>7m</c>,
    /// <c>3h</c>, <c>2d</c>, or a date once it stops being recent. A clock that ran
    /// backwards (a stored time in the future) reads as <c>now</c> rather than a negative.
    /// </summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;
        if (elapsed < TimeSpan.Zero)
        {
            return "now";
        }

        if (elapsed < TimeSpan.FromSeconds(45))
        {
            return "now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays}d";
        }

        return when.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
    }
}