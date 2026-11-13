namespace ClimberWeb.Services;

/// <summary>
/// A single external command invocation.
/// </summary>
/// <param name="FileName">Executable to launch, e.g. <c>opencode</c>, <c>dotnet</c> or <c>git</c>.</param>
/// <param name="Arguments">Arguments passed individually (no shell quoting needed).</param>
/// <param name="WorkingDirectory">Directory the command runs in.</param>
public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

/// <summary>
/// The captured result of a command invocation.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>
/// Runs external processes. Abstracted so the climb loop can be unit tested
/// without spawning real <c>opencode</c> / <c>dotnet</c> / <c>git</c> processes.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Runs <paramref name="request"/> to completion, streaming each output line to
    /// <paramref name="onOutputLine"/> as it arrives.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled; the process is killed first.
    /// </exception>
    Task<CommandResult> RunAsync(
        CommandRequest request,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken);
}
