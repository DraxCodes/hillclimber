using ClimberWeb.Services;
using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Tests;

public class ProjectStatusTests
{
    private static string Record(string hash, string date, string subject, string body = "") =>
        $"{hash}{GitHistory.FieldSeparator}{date}{GitHistory.FieldSeparator}{subject}{GitHistory.FieldSeparator}{body}{GitHistory.RecordSeparator}";

    // ---- git log parsing -----------------------------------------------------

    [Fact]
    public void ParseLog_ReadsHashSubjectAndDate()
    {
        var output = Record("abc1234", "2026-11-13T10:15:00+00:00", "docs: add a thing");

        var entries = GitHistory.ParseLog(output);

        var entry = Assert.Single(entries);
        Assert.Equal("abc1234", entry.Hash);
        Assert.Equal("docs: add a thing", entry.RawSubject);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 10, 15, 0, TimeSpan.Zero), entry.When);
    }

    [Fact]
    public void ParseLog_SkipsMalformedRecords()
    {
        var output = "no separators here" + GitHistory.RecordSeparator
            + Record("def5678", "2026-11-13T11:00:00+00:00", "fix: y");

        var entries = GitHistory.ParseLog(output);

        Assert.Equal("def5678", Assert.Single(entries).Hash);
    }

    [Fact]
    public void ParseLog_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(GitHistory.ParseLog(string.Empty));
        Assert.Empty(GitHistory.ParseLog(null));
    }

    // ---- description resolution ---------------------------------------------

    [Fact]
    public void ResolveDescription_PrefersTrailerOverEverything()
    {
        var sidecar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["abc1234"] = "From the sidecar",
        };

        var (description, authored) = GitHistory.ResolveDescription(
            "abc1234", "chore: whatever", "Some body\n\nDescription: Added the status page.\n", sidecar);

        Assert.Equal("Added the status page.", description);
        Assert.True(authored);
    }

    [Fact]
    public void ResolveDescription_FallsBackToSidecar()
    {
        var sidecar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["def5678"] = "Sidecar description",
        };

        var (description, authored) = GitHistory.ResolveDescription("def5678", "fix: y", "body", sidecar);

        Assert.Equal("Sidecar description", description);
        Assert.True(authored);
    }

    [Fact]
    public void ResolveDescription_FallsBackToDerivedSubject()
    {
        var (description, authored) = GitHistory.ResolveDescription(
            "aaa1111", "fix(services): show cache-write tokens", "no trailer here", null);

        Assert.Equal("Show cache-write tokens", description);
        Assert.False(authored);
    }

    [Theory]
    [InlineData("docs: add a thing", "Add a thing")]
    [InlineData("feat(ui)!: break it", "Break it")]
    [InlineData("plain subject", "Plain subject")]
    [InlineData("chore:", "Chore:")]
    public void SimplifySubject_StripsConventionalPrefix(string subject, string expected)
    {
        Assert.Equal(expected, GitHistory.SimplifySubject(subject));
    }

    // ---- cadence -------------------------------------------------------------

    [Fact]
    public void BuildCadence_UsesHoursWhenSpanIsShort()
    {
        var baseTime = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        var changes = new[]
        {
            new ChangeEntry("a", baseTime, "s", "d", true),
            new ChangeEntry("b", baseTime.AddMinutes(30), "s", "d", true),
        };

        var (unit, buckets) = GitHistory.BuildCadence(changes);

        Assert.Equal(CadenceUnit.Hour, unit);
        Assert.Equal(2, buckets.Sum(b => b.Count));
        Assert.All(buckets, b => Assert.Matches(@"^\d{2}:00$", b.Label));
    }

    [Fact]
    public void BuildCadence_UsesDaysWhenSpanIsLong()
    {
        var baseTime = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var changes = new[]
        {
            new ChangeEntry("a", baseTime, "s", "d", true),
            new ChangeEntry("b", baseTime.AddDays(3), "s", "d", true),
        };

        var (unit, buckets) = GitHistory.BuildCadence(changes);

        Assert.Equal(CadenceUnit.Day, unit);
        Assert.Equal(4, buckets.Count);
        Assert.Equal(2, buckets.Sum(b => b.Count));
    }

    [Fact]
    public void BuildCadence_Empty_IsEmpty()
    {
        var (_, buckets) = GitHistory.BuildCadence(Array.Empty<ChangeEntry>());
        Assert.Empty(buckets);
    }

    // ---- coverage artifact ---------------------------------------------------

    [Fact]
    public void CoverageArtifact_ParsesFullDocument()
    {
        const string json = """
            {
              "generated": "2026-11-13T09:00:00Z",
              "tests": { "passed": 77, "failed": 1, "skipped": 2 },
              "coverage": { "line": 0.83 }
            }
            """;

        var coverage = CoverageArtifact.Parse(json);

        Assert.NotNull(coverage);
        Assert.Equal(77, coverage!.Passed);
        Assert.Equal(1, coverage.Failed);
        Assert.Equal(2, coverage.Skipped);
        Assert.Equal(80, coverage.Total);
        Assert.Equal(83, coverage.LinePercent);
        Assert.False(coverage.Passing);
    }

    [Fact]
    public void CoverageArtifact_MissingFieldsDegradeToDefaults()
    {
        var coverage = CoverageArtifact.Parse("{}");

        Assert.NotNull(coverage);
        Assert.Equal(0, coverage!.Total);
        Assert.Null(coverage.LinePercent);
        Assert.True(coverage.Passing);
    }

    [Fact]
    public void CoverageArtifact_ClampsLineRate()
    {
        var coverage = CoverageArtifact.Parse("""{ "coverage": { "line": 1.4 } }""");
        Assert.Equal(100, coverage!.LinePercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    public void CoverageArtifact_UnusableInput_ReturnsNull(string? json)
    {
        Assert.Null(CoverageArtifact.Parse(json));
    }

    // ---- sidecar -------------------------------------------------------------

    [Fact]
    public void ChangelogSidecar_ParsesHashMap()
    {
        var map = ChangelogSidecar.Parse("""{ "abc1234": "Did a thing", "bad": 7 }""");

        Assert.Equal("Did a thing", map["abc1234"]);
        Assert.False(map.ContainsKey("bad"));
    }

    [Fact]
    public void ChangelogSidecar_InvalidJson_IsEmpty()
    {
        Assert.Empty(ChangelogSidecar.Parse("{ nope"));
    }

    // ---- issues --------------------------------------------------------------

    [Fact]
    public void IssueScanner_OpenIssueIsTracked()
    {
        const string content = "# Track something important\n\nStatus: open\nType: task\n";

        var issue = IssueScanner.Parse(content, "backlog", "01-thing.md");

        Assert.NotNull(issue);
        Assert.Equal("Track something important", issue!.Title);
        Assert.Equal("open", issue.Status);
        Assert.Equal("backlog", issue.Feature);
        Assert.Equal(1, issue.Number);
    }

    [Fact]
    public void IssueScanner_ResolvedIssueIsDropped()
    {
        Assert.Null(IssueScanner.Parse("Status: resolved\n# Done", "backlog", "01-thing.md"));
    }

    [Fact]
    public void IssueScanner_MissingStatusDefaultsToOpen()
    {
        var issue = IssueScanner.Parse("# No status here", "backlog", "02-thing.md");
        Assert.Equal("open", issue!.Status);
        Assert.Equal(2, issue.Number);
    }

    [Fact]
    public void IssueScanner_MissingScratchTree_IsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"climber-missing-{Guid.NewGuid():N}");
        Assert.Empty(IssueScanner.Scan(missing));
    }

    // ---- workflow ------------------------------------------------------------

    [Fact]
    public void WorkflowReader_ParsesAgentFrontMatter()
    {
        const string content = """
            ---
            description: Designs the plan
            mode: subagent
            model: openrouter/deepseek/deepseek-v4.1-flash#low
            permissions:
              - action: edit
                resource: "*"
                effect: deny
            ---
            Body
            """;

        var role = WorkflowCatalogReader.ParseAgent("plan-architect", content);

        Assert.Equal("plan-architect", role.Name);
        Assert.Equal("Designs the plan", role.Description);
        Assert.Equal("openrouter/deepseek/deepseek-v4.1-flash#low", role.Model);
        Assert.True(role.ReadOnly);
    }

    [Fact]
    public void WorkflowReader_AgentWithoutDenyIsNotReadOnly()
    {
        var role = WorkflowCatalogReader.ParseAgent("task-orchestrator", "description: Owns the run\nmodel: m");
        Assert.False(role.ReadOnly);
    }

    [Fact]
    public void WorkflowReader_ReadsAgentMode()
    {
        var primary = WorkflowCatalogReader.ParseAgent("task-orchestrator", "mode: primary\nmodel: m");
        var subagent = WorkflowCatalogReader.ParseAgent("plan-architect", "mode: subagent\nmodel: m");

        Assert.Equal("primary", primary.Mode);
        Assert.True(primary.IsPrimary);
        Assert.False(subagent.IsPrimary);
    }

    [Fact]
    public void WorkflowReader_ReadsDisplayNameFallingBackToTitleCase()
    {
        var named = WorkflowCatalogReader.ParseAgent("task-orchestrator", "display_name: Orchestrator\nmode: primary");
        var derived = WorkflowCatalogReader.ParseAgent("security-auditor", "mode: subagent");

        Assert.Equal("Orchestrator", named.DisplayName);
        Assert.Equal("Security Auditor", derived.DisplayName);
        Assert.Equal("Main", WorkflowCatalogReader.DisplayName(null));
    }

    // ---- root resolution -----------------------------------------------------

    [Fact]
    public void ResolveRepoRoot_ConfiguredPathWins()
    {
        var dir = Directory.CreateTempSubdirectory("climber-root").FullName;
        try
        {
            Assert.Equal(dir, ProjectStatusService.ResolveRepoRoot(dir, Path.GetTempPath()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveRepoRoot_WalksUpToGitEntry()
    {
        var root = Directory.CreateTempSubdirectory("climber-root").FullName;
        var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            Assert.Equal(new DirectoryInfo(root).FullName, ProjectStatusService.ResolveRepoRoot(null, nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- service -------------------------------------------------------------

    [Fact]
    public async Task GetStatusAsync_NoRepository_ReportsWarning()
    {
        var empty = Directory.CreateTempSubdirectory("climber-empty").FullName;
        try
        {
            var service = new ProjectStatusService(
                new FakeCommandRunner((_, _) => throw new InvalidOperationException("no git")),
                Config(),
                empty);

            var report = await service.GetStatusAsync();

            Assert.Null(report.Repo);
            Assert.Empty(report.Changes);
            Assert.NotEmpty(report.Warnings);
            Assert.Equal(WorkflowCatalogReader.BaseWorkflow, report.Workflow.BaseWorkflow);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public async Task GetStatusAsync_GitFailure_DoesNotThrow()
    {
        var root = Directory.CreateTempSubdirectory("climber-repo").FullName;
        try
        {
            var service = new ProjectStatusService(
                new FakeCommandRunner((_, _) => new CommandResult(128, string.Empty, "fatal: not a git repository")),
                Config(root),
                root);

            var report = await service.GetStatusAsync();

            Assert.Empty(report.Changes);
            Assert.Empty(report.Cadence);
            Assert.Contains(report.Warnings, w => w.Contains("git history", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IConfiguration Config(string? repoRoot = null)
    {
        var values = new Dictionary<string, string?>();
        if (repoRoot is not null)
        {
            values["Climber:RepoRoot"] = repoRoot;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
