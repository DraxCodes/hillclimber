using System.Security.Cryptography;
using System.Text;

namespace ClimberWeb.Services;

/// <summary>
/// Identity of the inputs a coverage measurement was produced from. The version guards
/// the rule itself: if the set of hashed files changes, the page reports "freshness
/// unknown" instead of comparing hashes produced by different rules.
/// </summary>
/// <param name="Version">Fingerprint rule version.</param>
/// <param name="Inputs">Lower-case SHA-256 hex over the measured inputs.</param>
public sealed record CoverageFingerprint(int Version, string? Inputs);

/// <summary>
/// Computes the fingerprint of everything that can change what the status artifact
/// measures: application code, tests and fixtures, harness scripts, styling and the
/// rating rubric. Build output and installed packages are ignored, and line endings are
/// normalised to LF so a checkout cannot change the result.
/// </summary>
public static class CoverageInputs
{
    /// <summary>Version of the fingerprint rule. Bump when the file set or hashing changes.</summary>
    public const int Version = 2;

    private static readonly string[] Roots = ["ClimberWeb", "ClimberWeb.Tests", "scripts"];

    private static readonly string[] Extensions =
        [".cs", ".razor", ".csproj", ".json", ".jsonl", ".ps1", ".mjs", ".css"];

    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    /// <summary>
    /// Hashes the working tree, or returns null when it cannot be read. The result is
    /// comparable only with a fingerprint recorded by the same <see cref="Version"/>.
    /// </summary>
    public static string? Capture(string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            var files = new List<string>();
            foreach (var directory in Roots)
            {
                var path = Path.Combine(root, directory);
                if (Directory.Exists(path))
                {
                    Collect(path, files);
                }
            }

            var rubric = Path.Combine(root, CoverageRubric.RelativePath);
            if (File.Exists(rubric))
            {
                files.Add(rubric);
            }

            if (files.Count == 0)
            {
                return null;
            }

            var relative = files
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .ToArray();
            Array.Sort(relative, StringComparer.Ordinal);

            using var buffer = new MemoryStream();
            foreach (var path in relative)
            {
                buffer.Write(Encoding.UTF8.GetBytes(path));
                buffer.WriteByte(10);
                buffer.Write(ReadNormalized(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))));
                buffer.WriteByte(10);
            }

            return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a file's bytes with CRLF collapsed to LF. Line endings are not semantics:
    /// git checks the same file out as CRLF on Windows and LF elsewhere, and a
    /// fingerprint that changed with the checkout would report a false "sources changed".
    /// </summary>
    private static byte[] ReadNormalized(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Array.IndexOf(bytes, (byte)'\r') < 0)
        {
            return bytes;
        }

        using var normalized = new MemoryStream(bytes.Length);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r'
                && index + 1 < bytes.Length
                && bytes[index + 1] == (byte)'\n')
            {
                continue;
            }

            normalized.WriteByte(bytes[index]);
        }

        return normalized.ToArray();
    }

    private static void Collect(string directory, List<string> files)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedDirectories.Contains(Path.GetFileName(child)))
            {
                Collect(child, files);
            }
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }
    }
}
