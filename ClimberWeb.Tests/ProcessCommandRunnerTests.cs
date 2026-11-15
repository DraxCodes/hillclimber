using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// Serialises the PATH-mutating test below with the rest of the suite: xUnit runs
/// collections in parallel, and that test temporarily edits the process-wide PATH.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessCommandRunnerCollection
{
    public const string Name = "ProcessCommandRunner";
}

/// <summary>
/// Integration-level checks for the real process-based runner. The children are
/// minimal, always-present executables — a shell that echoes a line, and <c>ping</c>
/// for a long-running process — so real process behaviour is covered without paying
/// the .NET host's startup cost on every run.
/// </summary>
[Collection(ProcessCommandRunnerCollection.Name)]
public class ProcessCommandRunnerTests
{
    private const string EchoedLine = "streamed-line";

    private readonly ProcessCommandRunner _runner = new();

    /// <summary>
    /// A minimal child that writes one line and exits. Spawning the shell costs about ten
    /// milliseconds, whereas <c>dotnet</c> pays the .NET host's ~50 ms startup on every
    /// run, which dominated this test's duration. The payload is a <c>const</c>, so no
    /// external value can reach the shell line.
    /// </summary>
    private static CommandRequest Echo() =>
        OperatingSystem.IsWindows()
            ? new CommandRequest(Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", $"echo {EchoedLine}" }, Directory.GetCurrentDirectory())
            : new CommandRequest("/bin/sh", new[] { "-c", $"echo {EchoedLine}" }, Directory.GetCurrentDirectory());

    [Fact]
    public async Task RunsProcess_CapturesOutput_AndStreamsLines()
    {
        var streamed = new List<string>();

        var result = await _runner.RunAsync(
            Echo(),
            line => { lock (streamed) streamed.Add(line); },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(EchoedLine, result.StandardOutput);
        Assert.Contains(streamed, line => line.Trim() == EchoedLine);
    }

    [Fact]
    public async Task ResolvesCmdShimOnPath_WhenGivenBareName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // PATHEXT and .cmd shims are Windows-only
        }

        var dir = Path.Combine(Path.GetTempPath(), "climber-shim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "climber-probe.cmd"), "@echo off\r\necho shim-ok\r\n");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + originalPath);

        try
        {
            var result = await _runner.RunAsync(
                new CommandRequest("climber-probe", Array.Empty<string>(), Directory.GetCurrentDirectory()),
                null,
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("shim-ok", result.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task CancelDuringRun_KillsProcess_AndThrows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // ping's -n flag and its output format are Windows-only
        }

        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pin the executable so a hijacked PATH cannot swap it.
        var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");

        var run = _runner.RunAsync(
            new CommandRequest(
                ping,
                new[] { "-n", "30", "127.0.0.1" },
                Directory.GetCurrentDirectory()),
            line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    started.TrySetResult();
                }
            },
            cts.Token);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            // Cancel even if the first line never arrives, so a failed wait cannot leave
            // the ping child running for its full 30 seconds.
            cts.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task WorksFromNonExistentWorkingDirectory_ReportsStartFailure()
    {
        // A missing working directory surfaces as an exception from Process.Start.
        await Assert.ThrowsAnyAsync<Exception>(() => _runner.RunAsync(
            new CommandRequest("dotnet", new[] { "--version" }, Path.Combine(Path.GetTempPath(), "climber-missing-" + Guid.NewGuid())),
            null,
            CancellationToken.None));
    }
}
