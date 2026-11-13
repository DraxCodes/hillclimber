using System.Globalization;
using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>
/// Parses <c>git log</c> output into changelog rows, derives the simplified
/// one-line description for each commit, and buckets commits for the cadence graph.
/// </summary>
public static partial class GitHistory
{
    /// <summary>Field separator used by the log format (<c>%x1f</c>).</summary>
    public const char FieldSeparator = '\u001f';

    /// <summary>Record separator used by the log format (<c>%x1e</c>).</summary>
    public const char RecordSeparator = '\u001e';

    /// <summary>
    /// The <c>--pretty</c> format the service passes to git: hash, author date, subject,
    /// body, each separated by <see cref="FieldSeparator"/> and terminated by
    /// <see cref="RecordSeparator"/>.
    /// </summary>
    public const string PrettyFormat = "%h%x1f%aI%x1f%s%x1f%b%x1e";

    [GeneratedRegex(@"^[A-Za-z]+(?:\([^)]*\))?!?:\s*")]
    private static partial Regex ConventionalPrefix();

    [GeneratedRegex(@"^Description:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionTrailer();

    /// <summary>
    /// Parses the output of <c>git log --pretty=format:&lt;PrettyFormat&gt;</c>.
    /// Malformed records are skipped rather than throwing.
    /// </summary>
    /// <param name="output">Raw git stdout.</param>
    /// <param name="sidecar">Hash-to-description sidecar; may be empty.</param>
    public static IReadOnlyList<ChangeEntry> ParseLog(
        string? output,
        IReadOnlyDictionary<string, string>? sidecar = null)
    {
        var entries = new List<ChangeEntry>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return entries;
        }

        foreach (var raw in output.Split(RecordSeparator))
        {
            var record = raw.Trim('\n', '\r', ' ', '\t');
            if (record.Length == 0)
            {
                continue;
            }

            var fields = record.Split(FieldSeparator, 4);
            if (fields.Length < 3)
            {
                continue;
            }

            var hash = fields[0].Trim();
            if (hash.Length == 0)
            {
                continue;
            }

            var when = DateTimeOffset.TryParse(
                fields[1].Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            var subject = fields[2].Trim();
            var body = fields.Length > 3 ? fields[3] : string.Empty;
            var (description, authored) = ResolveDescription(hash, subject, body, sidecar);
            entries.Add(new ChangeEntry(hash, when, subject, description, authored));
        }

        return entries;
    }

    /// <summary>
    /// Resolves a commit's simplified description, preferring an explicit source:
    /// a <c>Description:</c> trailer, then the sidecar, then the subject with its
    /// conventional-commit prefix stripped.
    /// </summary>
    public static (string Description, bool Authored) ResolveDescription(
        string hash,
        string subject,
        string? body,
        IReadOnlyDictionary<string, string>? sidecar)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            var trailer = DescriptionTrailer().Match(body);
            if (trailer.Success && trailer.Groups[1].Value.Trim() is { Length: > 0 } value)
            {
                return (value, true);
            }
        }

        if (sidecar is { Count: > 0 })
        {
            foreach (var key in CandidateKeys(hash))
            {
                if (sidecar.TryGetValue(key, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                {
                    return (mapped.Trim(), true);
                }
            }
        }

        return (SimplifySubject(subject), false);
    }

    private static IEnumerable<string> CandidateKeys(string hash)
    {
        yield return hash;
        if (hash.Length > 7)
        {
            yield return hash[..7];
        }
    }

    /// <summary>Strips a conventional-commit prefix and capitalises the result.</summary>
    public static string SimplifySubject(string subject)
    {
        var trimmed = subject.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var stripped = ConventionalPrefix().Replace(trimmed, string.Empty).Trim();
        if (stripped.Length == 0)
        {
            stripped = trimmed;
        }

        return char.ToUpperInvariant(stripped[0]) + stripped[1..];
    }

    /// <summary>Buckets by hour or day, filling gaps so the graph is contiguous.</summary>
    public static (CadenceUnit Unit, IReadOnlyList<CadenceBucket> Buckets) BuildCadence(
        IReadOnlyList<ChangeEntry> changes)
    {
        if (changes.Count == 0)
        {
            return (CadenceUnit.Day, Array.Empty<CadenceBucket>());
        }

        var times = changes
            .Select(c => c.When.ToLocalTime())
            .Where(t => t != DateTimeOffset.MinValue)
            .ToList();

        if (times.Count == 0)
        {
            return (CadenceUnit.Day, Array.Empty<CadenceBucket>());
        }

        var min = times.Min();
        var max = times.Max();
        var unit = max - min < TimeSpan.FromHours(36) ? CadenceUnit.Hour : CadenceUnit.Day;

        var counts = new Dictionary<DateTimeOffset, int>();
        foreach (var time in times)
        {
            var key = Truncate(time, unit);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var start = Truncate(min, unit);
        var end = Truncate(max, unit);
        var buckets = new List<CadenceBucket>();
        for (var cursor = start; cursor <= end && buckets.Count < 366; cursor = Advance(cursor, unit))
        {
            buckets.Add(new CadenceBucket(cursor, Label(cursor, unit), counts.GetValueOrDefault(cursor)));
        }

        return (unit, buckets);
    }

    private static DateTimeOffset Truncate(DateTimeOffset value, CadenceUnit unit) => unit switch
    {
        CadenceUnit.Hour => new DateTimeOffset(
            value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset),
        _ => new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset),
    };

    private static DateTimeOffset Advance(DateTimeOffset value, CadenceUnit unit) =>
        unit == CadenceUnit.Hour ? value.AddHours(1) : value.AddDays(1);

    private static string Label(DateTimeOffset value, CadenceUnit unit) => unit switch
    {
        CadenceUnit.Hour => value.ToString("HH:00", CultureInfo.InvariantCulture),
        _ => value.ToString("MMM d", CultureInfo.InvariantCulture),
    };
}
