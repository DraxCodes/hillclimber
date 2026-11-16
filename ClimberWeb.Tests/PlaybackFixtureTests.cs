using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The committed playback fixtures. They are the reproducible substitute for the hand-written
/// seed that was created and deleted three times: real opencode event shapes, replayed through
/// the real <see cref="LogFormatter"/> and <see cref="LogGrouper"/>, behind a Development gate.
/// </summary>
public class PlaybackFixtureTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string[] Lines(string name) =>
        File.ReadAllLines(Fixture(name)).Where(line => line.Trim().Length > 0).ToArray();

    [Fact]
    public void EveryFixtureLineIsAnEventTheFormatterRecognises()
    {
        foreach (var name in new[] { PlaybackRunService.DefaultFixture, PlaybackRunService.LiveFixture })
        {
            foreach (var line in Lines(name))
            {
                var entry = LogFormatter.Parse(line);

                Assert.True(
                    entry.Kind != LogKind.Plain,
                    $"{name} contains a line the renderer degrades to raw text: {line[..Math.Min(80, line.Length)]}");
            }
        }
    }

    [Fact]
    public void TheDefaultTranscriptGroupsIntoMessagesTurnsAndQuietRows()
    {
        var items = LogGrouper.Group(Lines(PlaybackRunService.DefaultFixture).Select(LogFormatter.Parse).ToArray());

        Assert.Equal(3, items.Count(item => item.Kind == OutputItemKind.Turn));
        Assert.Single(items, item => item.Kind == OutputItemKind.Message);
        Assert.Single(items, item => item.Kind == OutputItemKind.Marker);
        Assert.Single(items, item => item.Kind == OutputItemKind.Error);
        Assert.Equal(2, items.Count(item => item.Kind == OutputItemKind.Unrecognized));
        Assert.All(items.Where(item => item.Kind == OutputItemKind.Turn), item => Assert.NotNull(item.Footer));
        Assert.DoesNotContain(items, item => item.Open);
    }

    [Fact]
    public void TheDefaultTranscriptExercisesEveryStateTheUiHasToRender()
    {
        var entries = Lines(PlaybackRunService.DefaultFixture).Select(LogFormatter.Parse).ToArray();
        var items = LogGrouper.Group(entries);

        // The header strip needs more than one delegate, the error row needs a real error,
        // the sources row needs an http input, and the unknown-event row needs a type the
        // renderer does not special-case.
        Assert.True(entries.Count(entry => entry.Kind == LogKind.Subagent) >= 3);
        Assert.Contains(entries, entry => entry.Kind == LogKind.Tool && entry.Status == StepStatus.Failed);
        Assert.Contains(entries, entry => entry.Kind == LogKind.Tool && entry.Text == "websearch");
        Assert.Contains(entries, entry => entry.Kind == LogKind.Unrecognized && entry.Text == "message.updated");
        Assert.Contains(entries, entry => entry.Kind == LogKind.Unrecognized && entry.Text == "session.diff");
        Assert.Contains(items, item => item.Kind == OutputItemKind.Message);
    }

    [Fact]
    public void TheLiveTranscriptEndsInAnOpenTurn()
    {
        var items = LogGrouper.Group(Lines(PlaybackRunService.LiveFixture).Select(LogFormatter.Parse).ToArray());

        var last = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, last.Kind);
        Assert.True(last.Open);
        Assert.Contains(last.Entries, entry => entry.Kind == LogKind.Tool && entry.Status == StepStatus.Running);
    }

    [Fact]
    public void PlaybackBuildsTheStateTheRunWouldHaveWritten()
    {
        var start = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        var playback = PlaybackRunService.Create(Fixture(PlaybackRunService.DefaultFixture), start);

        Assert.Equal(RunStatus.Completed, playback.State.Status);
        // Completed carries no message: the status word is the statement.
        Assert.Null(playback.State.Message);
        Assert.Equal(start, playback.State.StartedAt);
        Assert.NotNull(playback.State.FinishedAt);
        Assert.Equal(Lines(PlaybackRunService.DefaultFixture).Length, playback.State.Logs.Count);
        Assert.All(playback.State.Logs, line => Assert.StartsWith("[09:", line));
        Assert.Equal(new RunMetrics(0.0124m, 6700, 1416, 18432, 640), playback.State.Metrics);
        Assert.False(playback.IsRunning);
    }

    [Fact]
    public void PlaybackMarksTheLiveFixtureRunning()
    {
        var playback = PlaybackRunService.Create(Fixture(PlaybackRunService.LiveFixture));

        Assert.Equal(RunStatus.Running, playback.State.Status);
        Assert.Null(playback.State.FinishedAt);
        Assert.True(playback.IsRunning);
    }

    [Fact]
    public async Task PlaybackCannotStartARealRun()
    {
        var playback = PlaybackRunService.Create(Fixture(PlaybackRunService.DefaultFixture));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => playback.StartAsync(new RunOptions(".", "do the thing")));

        Assert.Contains(PlaybackRunService.EnvVar, error.Message);
    }

    [Fact]
    public void FixtureNamesResolveToTheCommittedTranscripts()
    {
        var root = AppContext.BaseDirectory;

        Assert.EndsWith(PlaybackRunService.DefaultFixture, PlaybackRunService.ResolveFixture("1", root));
        Assert.EndsWith(PlaybackRunService.LiveFixture, PlaybackRunService.ResolveFixture("live", root));
        Assert.Throws<FileNotFoundException>(() => PlaybackRunService.ResolveFixture("no-such-fixture", root));
    }

    [Fact]
    public void AnAbsoluteOrRelativePathSeedResolvesToTheNamedFile()
    {
        var absolute = Path.GetFullPath(Fixture(PlaybackRunService.DefaultFixture));
        Assert.Equal(absolute, PlaybackRunService.ResolveFixture(absolute, AppContext.BaseDirectory));

        var relative = Path.Combine("Fixtures", PlaybackRunService.LiveFixture);
        Assert.Equal(
            Path.GetFullPath(Fixture(PlaybackRunService.LiveFixture)),
            PlaybackRunService.ResolveFixture(relative, AppContext.BaseDirectory));
    }

    [Fact]
    public async Task PlaybackCannotStartAHillclimb()
    {
        var guard = new PlaybackHillClimberService();

        Assert.False(guard.IsRunning);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.StartAsync(new ClimberOptions(".", "climb the tests")));

        Assert.Contains(PlaybackRunService.EnvVar, error.Message);
    }

    [Fact]
    public void TheFixtureGateNeedsDevelopmentAndTheEnvironmentVariable()
    {
        WithSeed(null, () => Assert.False(PlaybackRunService.IsEnabled(isDevelopment: true)));
        WithSeed("1", () =>
        {
            Assert.True(PlaybackRunService.IsEnabled(isDevelopment: true));
            Assert.False(PlaybackRunService.IsEnabled(isDevelopment: false));
        });
    }

    [Fact]
    public void PlaybackRefusesToBuildOutsideDevelopmentEvenWithTheVariableSet()
    {
        WithSeed("1", () =>
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => PlaybackRunService.FromEnvironment(isDevelopment: false, AppContext.BaseDirectory));

            Assert.Contains("development", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void WithSeed(string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(PlaybackRunService.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PlaybackRunService.EnvVar, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(PlaybackRunService.EnvVar, previous);
        }
    }
}