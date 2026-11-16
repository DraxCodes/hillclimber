using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class CoverageInputsTests
{
    [Fact]
    public void Capture_IsStableAndSensitiveToContent()
    {
        var root = Directory.CreateTempSubdirectory("climber-fingerprint").FullName;
        try
        {
            var source = Path.Combine(root, "ClimberWeb");
            Directory.CreateDirectory(source);
            var file = Path.Combine(source, "Thing.cs");
            File.WriteAllText(file, "class Thing { }");

            var first = CoverageInputs.Capture(root);
            var second = CoverageInputs.Capture(root);

            Assert.NotNull(first);
            Assert.Equal(first, second);

            File.WriteAllText(file, "class Thing { int Value; }");

            Assert.NotEqual(first, CoverageInputs.Capture(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Capture_IgnoresBuildOutputAndUnmeasuredFiles()
    {
        var root = Directory.CreateTempSubdirectory("climber-fingerprint").FullName;
        try
        {
            var source = Path.Combine(root, "ClimberWeb");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Thing.cs"), "class Thing { }");

            var baseline = CoverageInputs.Capture(root);

            foreach (var directory in new[] { "bin", "obj", "node_modules" })
            {
                var nested = Directory.CreateDirectory(Path.Combine(root, "ClimberWeb", directory)).FullName;
                File.WriteAllText(Path.Combine(nested, "Generated.cs"), "class Generated { }");
            }

            File.WriteAllText(Path.Combine(source, "notes.txt"), "not measured");

            Assert.Equal(baseline, CoverageInputs.Capture(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Capture_IncludesTheRubric()
    {
        var root = Directory.CreateTempSubdirectory("climber-fingerprint").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "ClimberWeb"));
            File.WriteAllText(Path.Combine(root, "ClimberWeb", "Thing.cs"), "class Thing { }");

            var baseline = CoverageInputs.Capture(root);

            Directory.CreateDirectory(Path.Combine(root, "status"));
            File.WriteAllText(Path.Combine(root, "status", "coverage-rubric.json"), "{}");

            Assert.NotEqual(baseline, CoverageInputs.Capture(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Capture_IgnoresLineEndings()
    {
        var root = Directory.CreateTempSubdirectory("climber-fingerprint").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "ClimberWeb")).FullName;
            var file = Path.Combine(source, "Thing.cs");

            File.WriteAllText(file, "class Thing {\n    int Value;\n}\n");
            var unix = CoverageInputs.Capture(root);

            File.WriteAllText(file, "class Thing {\r\n    int Value;\r\n}\r\n");

            Assert.NotNull(unix);
            Assert.Equal(unix, CoverageInputs.Capture(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Capture_MissingRepository_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"climber-missing-{Guid.NewGuid():N}");
        Assert.Null(CoverageInputs.Capture(missing));
    }
}
