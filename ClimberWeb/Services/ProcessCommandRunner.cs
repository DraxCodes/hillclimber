using System.Diagnostics;
using System.Text;

namespace ClimberWeb.Services;

/// <summary>
/// Default <see cref="ICommandRunner"/> backed by <see cref="Process"/>.
/// Streams stdout and stderr line-by-line and kills the whole process tree on cancellation.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        CommandRequest request,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        void Handle(DataReceivedEventArgs e, StringBuilder sink)
        {
            if (e.Data is null)
            {
                return;
            }

            lock (sink)
            {
                sink.AppendLine(e.Data);
            }

            onOutputLine?.Invoke(e.Data);
        }

        process.OutputDataReceived += (_, e) => Handle(e, standardOutput);
        process.ErrorDataReceived += (_, e) => Handle(e, standardError);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Close stdin so tools that read from it (e.g. opencode prompts) don't block.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Process may have already exited.
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Flushes any remaining async output callbacks.
        process.WaitForExit();

        string output;
        string error;
        lock (standardOutput)
        {
            output = standardOutput.ToString();
        }

        lock (standardError)
        {
            error = standardError.ToString();
        }

        return new CommandResult(process.ExitCode, output, error);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (NotSupportedException)
        {
            // Platform does not support process trees; fall back to a plain kill.
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
