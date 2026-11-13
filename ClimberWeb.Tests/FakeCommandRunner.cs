using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// Test double for <see cref="ICommandRunner"/>. Each invocation is passed to a handler
/// along with a zero-based invocation index so tests can script a sequence of results.
/// </summary>
internal sealed class FakeCommandRunner : ICommandRunner
{
    private readonly Func<CommandRequest, int, CancellationToken, Task<CommandResult>> _handler;
    private readonly object _gate = new();
    private readonly List<CommandRequest> _requests = new();
    private int _invocation;

    public FakeCommandRunner(Func<CommandRequest, int, CommandResult> handler)
        : this((request, index, _) => Task.FromResult(handler(request, index)))
    {
    }

    public FakeCommandRunner(Func<CommandRequest, int, CancellationToken, Task<CommandResult>> handler)
    {
        _handler = handler;
    }

    public IReadOnlyList<CommandRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public IReadOnlyList<CommandRequest> RequestsFor(string fileName) =>
        Requests.Where(r => r.FileName == fileName).ToArray();

    public Task<CommandResult> RunAsync(
        CommandRequest request,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        int index;
        lock (_gate)
        {
            _requests.Add(request);
            index = _invocation++;
        }

        var task = _handler(request, index, cancellationToken);

        if (task.IsCompletedSuccessfully)
        {
            var result = task.Result;
            Emit(result, onOutputLine);
            return task;
        }

        return AwaitAndEmit(task, onOutputLine);
    }

    private static async Task<CommandResult> AwaitAndEmit(Task<CommandResult> task, Action<string>? onOutputLine)
    {
        var result = await task.ConfigureAwait(false);
        Emit(result, onOutputLine);
        return result;
    }

    private static void Emit(CommandResult result, Action<string>? onOutputLine)
    {
        if (onOutputLine is null)
        {
            return;
        }

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            onOutputLine(line);
        }
    }
}

/// <summary>Convenience builders for scripted command results.</summary>
internal static class CannedResults
{
    public static CommandResult OpenCode(int exitCode = 0) =>
        new(exitCode, "{\"type\":\"result\",\"status\":\"ok\"}", string.Empty);

    public static CommandResult Git() => new(0, string.Empty, string.Empty);

    /// <summary>A realistic <c>dotnet test</c> summary line.</summary>
    public static CommandResult Tests(int passed, int failed = 0, int exitCode = 0) =>
        new(
            exitCode,
            $"Passed!  - Failed:     {failed}, Passed:     {passed}, Skipped:     0, Total:     {passed + failed}, Duration: 250 ms",
            string.Empty);

    public static CommandResult Raw(string stdout, int exitCode = 0) => new(exitCode, stdout, string.Empty);
}
