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
            FileName = ResolveExecutable(request.FileName, out var notFound),
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (notFound)
        {
            // A bare command name that is not on PATH: report it like any other failed
            // command so the caller can degrade gracefully instead of Process.Start
            // throwing an opaque "file not found".
            return new CommandResult(127, string.Empty, $"{request.FileName}: command not found");
        }

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

    /// <summary>
    /// Resolves a command name to a launchable path. <paramref name="notFound"/> is true when
    /// a bare name is not on <c>PATH</c>, so the caller can report a missing tool rather than
    /// letting <see cref="Process.Start(ProcessStartInfo)"/> throw an opaque error. A name
    /// that is already a path is returned as-is (the caller owns that path's existence).
    /// </summary>
    private static string ResolveExecutable(string fileName, out bool notFound)
    {
        notFound = false;

        if (Path.IsPathRooted(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            return fileName;
        }

        if (FindOnPath(fileName) is { } resolved)
        {
            return resolved;
        }

        notFound = true;
        return fileName;
    }

    /// <summary>
    /// Resolves a bare command name against <c>PATH</c> (honouring <c>PATHEXT</c> on
    /// Windows), returning the full path when it exists or null when it does not. Used
    /// both to launch shims and by the pre-flight probe to report missing tools.
    /// </summary>
    public static string? FindOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string[] extensions;
        if (OperatingSystem.IsWindows())
        {
            extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // A name that already ends in a known executable extension is searched as-is;
            // anything else (including dotted names with no real extension) is suffixed.
            if (extensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                extensions = [string.Empty];
            }
        }
        else
        {
            extensions = [string.Empty];
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var folder = directory.Trim('"');
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, fileName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
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
