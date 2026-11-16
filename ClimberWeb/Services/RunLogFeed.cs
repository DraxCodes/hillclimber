namespace ClimberWeb.Services;

/// <summary>
/// The bounded, timestamped log feed a run renders. Stamps each line with the local
/// time and trims the oldest once <see cref="MaxLines"/> is reached, so a run's feed
/// cannot grow without limit. Not thread-safe on its own; callers hold their own lock.
/// </summary>
internal sealed class RunLogFeed
{
    /// <summary>Most lines ever kept in the feed.</summary>
    public const int MaxLines = 5000;

    private readonly List<string> _lines = new();

    /// <summary>Stamps <paramref name="line"/> with the current time and appends it, returning the stamped line.</summary>
    public string Append(string line)
    {
        var stamped = Stamp(line, DateTime.Now);
        _lines.Add(stamped);
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }

        return stamped;
    }

    /// <summary>
    /// The one place that decides a transcript line's stamp. The feed uses the wall clock;
    /// the playback fixture passes a synthetic clock so screenshots repeat.
    /// </summary>
    public static string Stamp(string line, DateTimeOffset at) => $"[{at:HH:mm:ss}] {line}";

    public void Clear() => _lines.Clear();

    /// <summary>A point-in-time copy of the feed, oldest first.</summary>
    public IReadOnlyList<string> Snapshot() => _lines.ToArray();
}
