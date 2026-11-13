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
/// Integration-level checks for the real process-based runner. These use <c>dotnet</c>,
/// which is guaranteed to be present because the tests themselves run on it.
/// </summary>
[Collection(ProcessCommandRunnerCollection.Name)]
public class ProcessCommandRunnerTests
{
    private readonly ProcessCommandRunner _runner = new();

    [Fact]
    public async Task RunsProcess_CapturesOutput_AndStreamsLines()
    {
        var streamed = new List<string>();

        var result = await _runner.RunAsync(
            new CommandRequest("dotnet", new[] { "--version" }, Directory.GetCurrentDirectory()),
            line => { lock (streamed) streamed.Add(line); },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains(streamed, line => line.Trim().Length > 0);
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
            return; // exercises cmd.exe and ping, which are Windows-only
        }

        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = _runner.RunAsync(
            new CommandRequest(
                "cmd.exe",
                new[] { "/c", "echo ready & ping -n 30 127.0.0.1 >nul" },
                Directory.GetCurrentDirectory()),
            line =>
            {
                if (line.Contains("ready", StringComparison.OrdinalIgnoreCase))
                {
                    started.TrySetResult();
                }
            },
            cts.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

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
