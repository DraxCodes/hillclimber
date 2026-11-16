namespace ClimberWeb.Services;

/// <summary>
/// A dev-only transcript fixture: replays a recorded opencode event stream through the same
/// <see cref="LogFormatter"/> / <see cref="LogGrouper"/> pipeline the page renders, so a UI
/// pass, a reviewer or the quality gate starts from the same states every time. The event
/// shapes are real (one JSON object per line, exactly what <c>opencode run --format json</c>
/// emits); only the source is a file.
/// </summary>
/// <remarks>
/// Enabled only when the host environment is Development and <see cref="EnvVar"/> names a
/// fixture (<c>1</c> for the default transcript, <c>live</c> for the streaming one, or a
/// path). <see cref="FromEnvironment"/> refuses any other environment, and
/// <c>Program.cs</c> registers it only behind <c>IsDevelopment()</c>, so a Production host
/// cannot reach it even with the variable set. A playback host never drives opencode:
/// <see cref="StartAsync"/> fails with instructions instead of pretending to run.
/// </remarks>
public sealed class PlaybackRunService : IRunService
{
    /// <summary>The variable that turns a Development host into a fixture host.</summary>
    public const string EnvVar = "CLIMBER_SEED";

    /// <summary>The complete transcript <c>CLIMBER_SEED=1</c> replays.</summary>
    public const string DefaultFixture = "opencode-transcript.jsonl";

    /// <summary>The transcript that ends mid-turn <c>CLIMBER_SEED=live</c> replays.</summary>
    public const string LiveFixture = "opencode-transcript-live.jsonl";

    /// <summary>Where the committed fixtures live, relative to the repository root.</summary>
    public const string FixtureFolder = "ClimberWeb.Tests/Fixtures";

    private const int SecondsBetweenEvents = 4;

    private PlaybackRunService(RunState state) => State = state;

    /// <summary>The fixed snapshot built from the fixture.</summary>
    public RunState State { get; }

    /// <summary>Never raised: the transcript does not change while the host runs.</summary>
    public event Action<RunState>? StateChanged
    {
        add { }
        remove { }
    }

    public bool IsRunning => State.Status == RunStatus.Running;

    /// <summary>
    /// A fixture host is a display, not a runner. Fail with the way out instead of starting a
    /// real run on a service that has no command runner behind it.
    /// </summary>
    public Task StartAsync(RunOptions options) => throw new InvalidOperationException(
        $"This host was started with {EnvVar}; it only replays the committed transcript. " +
        $"Unset {EnvVar} and restart to drive opencode.");

    public void Stop()
    {
    }

    public Task ApproveFallbackAsync() => throw new InvalidOperationException(
        $"This host was started with {EnvVar}; it only replays the committed transcript.");

    /// <summary>
    /// True only in Development with <see cref="EnvVar"/> set. The environment is a parameter,
    /// not a lookup, so the refusal is testable and the caller cannot forget the gate.
    /// </summary>
    public static bool IsEnabled(bool isDevelopment) =>
        isDevelopment && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar));

    /// <summary>
    /// Builds the fixture host from <see cref="EnvVar"/>. Throws in any non-Development
    /// environment, which is the other half of the Production gate.
    /// </summary>
    public static PlaybackRunService FromEnvironment(bool isDevelopment, string contentRoot)
    {
        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                $"{nameof(PlaybackRunService)} is a development fixture and cannot be enabled outside Development.");
        }

        var seed = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(seed))
        {
            throw new InvalidOperationException($"Set {EnvVar} to a fixture name or path.");
        }

        return Create(ResolveFixture(seed.Trim(), contentRoot));
    }

    /// <summary>
    /// Resolves <c>1</c>/<c>true</c>/<c>on</c> to the default transcript, <c>live</c> to the
    /// streaming one, and anything else to a path (absolute, or relative to the content root
    /// or to a parent of it, which is how <c>dotnet run --project ClimberWeb</c> finds the
    /// fixtures under <c>ClimberWeb.Tests/Fixtures</c>).
    /// </summary>
    /// <exception cref="FileNotFoundException">No readable fixture matches the value.</exception>
    public static string ResolveFixture(string seed, string contentRoot)
    {
        var name = seed.ToLowerInvariant() switch
        {
            "1" or "true" or "on" => DefaultFixture,
            "live" => LiveFixture,
            _ => seed,
        };

        var candidates = new List<string>();
        if (Path.IsPathRooted(name))
        {
            candidates.Add(name);
        }
        else
        {
            for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
            {
                candidates.Add(Path.Combine(directory.FullName, FixtureFolder, name));
                candidates.Add(Path.Combine(directory.FullName, name));
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"{EnvVar} names '{seed}', but no fixture was found. Tried: {string.Join(", ", candidates)}");
    }

    /// <summary>
    /// Reads a JSONL fixture and builds the state the page would have seen had the run written
    /// those events: each line is validated by the real formatter, grouped by the real grouper
    /// (which decides whether the transcript ends open), stamped the way the log feed stamps,
    /// and summed the way the run sums its <c>step_finish</c> usage.
    /// </summary>
    public static PlaybackRunService Create(string fixturePath, DateTimeOffset? startedAt = null)
    {
        var lines = File.ReadAllLines(fixturePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            throw new InvalidDataException($"The playback fixture '{fixturePath}' is empty.");
        }

        // Replay through the page's own pipeline before exposing the state, so a fixture the
        // renderer cannot read fails here rather than in a screenshot.
        var items = LogGrouper.Group(lines.Select(LogFormatter.Parse).ToArray());

        var start = startedAt ?? DateTimeOffset.Now.AddMinutes(-9);
        var logs = new List<string>(lines.Length);
        var metrics = RunMetrics.Zero;
        var clock = start;
        foreach (var line in lines)
        {
            logs.Add($"[{clock:HH:mm:ss}] {line}");
            metrics = metrics.Add(line);
            clock = clock.AddSeconds(SecondsBetweenEvents);
        }

        // An open tail is the fixture's own statement that the run is still streaming.
        var live = items.Count > 0 && items[^1].Open;

        return new PlaybackRunService(new RunState
        {
            Status = live ? RunStatus.Running : RunStatus.Completed,
            Message = live ? "working" : "Run complete.",
            Logs = logs,
            Metrics = metrics,
            StartedAt = start,
            FinishedAt = live ? null : clock,
        });
    }
}