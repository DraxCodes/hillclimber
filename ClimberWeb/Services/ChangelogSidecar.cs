using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>
/// Reads the committed changelog sidecar (<c>status/changelog.json</c>), a
/// hash-to-description map that backfills simplified descriptions for commits that
/// predate the <c>Description:</c> trailer.
/// </summary>
public static class ChangelogSidecar
{
    /// <summary>Repository-relative path of the sidecar.</summary>
    public const string RelativePath = "status/changelog.json";

    /// <summary>Parses the sidecar JSON into a case-insensitive hash map.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return map;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 } description)
                {
                    map[property.Name.Trim()] = description.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // A broken sidecar is non-fatal: descriptions fall back to the subject.
        }

        return map;
    }

    /// <summary>Reads and parses the sidecar at <paramref name="path"/>.</summary>
    public static IReadOnlyDictionary<string, string> ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Parse(null);
        }
        catch (IOException)
        {
            return Parse(null);
        }
        catch (UnauthorizedAccessException)
        {
            return Parse(null);
        }
    }
}
