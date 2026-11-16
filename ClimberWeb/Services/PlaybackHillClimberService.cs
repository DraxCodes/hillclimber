namespace ClimberWeb.Services;

/// <summary>
/// The hillclimb loop as a fixture host sees it: it refuses to start. The loop runs
/// <c>opencode</c> and <c>dotnet test</c> itself, so a replayed transcript cannot stand in for
/// it. Without this guard, choosing hillclimb with <see cref="PlaybackRunService.EnvVar"/> set
/// would launch a real, paid run, and the fixture host's "never drives opencode" promise would
/// be false.
/// </summary>
public sealed class PlaybackHillClimberService : IHillClimberService
{
    /// <summary>Always idle: the loop never runs on a fixture host.</summary>
    public ClimberState State { get; } = new();

    /// <summary>Never raised: the state cannot change.</summary>
    public event Action<ClimberState>? StateChanged
    {
        add { }
        remove { }
    }

    public bool IsRunning => false;

    /// <summary>Fails with the way out instead of starting a real climb.</summary>
    public Task StartAsync(ClimberOptions options) => throw new InvalidOperationException(
        $"This host was started with {PlaybackRunService.EnvVar}; it only replays the committed transcript. " +
        $"Unset {PlaybackRunService.EnvVar} and restart to run a hillclimb.");

    public void Stop()
    {
    }

    public void ApproveFallback() => throw new InvalidOperationException(
        $"This host was started with {PlaybackRunService.EnvVar}; it only replays the committed transcript.");
}