namespace Digitals.AsyncApi.Tests;

/// <summary>The commands CI runs, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CommandTests
{
    [Fact]
    public void Lint_CleanDocument_ExitsZero() =>
        Assert.Equal(0, Run(RulesetTests.Clean).ExitCode);

    [Fact]
    public void Lint_BrokenRule_ExitsOneAndNamesIt()
    {
        var (exitCode, output) = Run(
            RulesetTests.Clean.Replace("noteId: { type: string", "note_id: { type: string", StringComparison.Ordinal));

        Assert.Equal(1, exitCode);
        Assert.Contains("dt-property-casing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_BaselineAndRuleset_IsAUsageError() =>
        Assert.Equal(2, Run(RulesetTests.Clean, "--baseline", "--ruleset", "digitals.yaml").ExitCode);

    [Fact]
    public void Ruleset_Baseline_PrintsOnlyTheBasics()
    {
        var (exitCode, output) = Spectral.Tool("ruleset", "--baseline");

        Assert.Equal(0, exitCode);
        Assert.Contains("dt-no-personal-data-in-channel", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dt-property-casing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_ExtendingRuleset_KeepsEveryOtherRule()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.Equal(0, Spectral.Tool("ruleset", Path.Combine(folder, "digitals.yaml")).ExitCode);
            var custom = Path.Combine(folder, "custom.yaml");
            File.WriteAllText(custom, "extends:\n  - ./digitals.yaml\nrules:\n  dt-property-casing: off\n");

            var snakeCase = RulesetTests.Clean
                .Replace("noteId: { type: string", "note_id: { type: string", StringComparison.Ordinal);
            var noDescription = RulesetTests.Clean
                .Replace(", description: The note's text, on one line. }", " }", StringComparison.Ordinal);
            Assert.Equal(0, Run(snakeCase, "--ruleset", custom).ExitCode);
            Assert.Equal(1, Run(noDescription, "--ruleset", custom).ExitCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Docs_WritesAPageThatNeedsNoNetwork()
    {
        var spec = Spectral.Write(RulesetTests.Clean);
        var page = Path.ChangeExtension(spec, ".html");
        try
        {
            Assert.Equal(0, Spectral.Tool("docs", spec).ExitCode);

            var html = File.ReadAllText(page);
            Assert.Contains("AsyncApiStandalone.render(", html, StringComparison.Ordinal);
            Assert.Contains("Events the notes service publishes when a note changes.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("%SPEC%", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script src", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(spec);
            File.Delete(page);
        }
    }

    [Fact]
    public void Docs_GivenOutput_WritesThere()
    {
        var spec = Spectral.Write(RulesetTests.Clean);
        var page = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            Assert.Equal(0, Spectral.Tool("docs", spec, page).ExitCode);
            Assert.Contains("AsyncApiStandalone.render(", File.ReadAllText(page), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(spec);
            File.Delete(page);
        }
    }

    [Fact]
    public void Spectral_RunsTheBundledTool() =>
        Assert.Equal(0, Spectral.Tool("spectral", "--version").ExitCode);

    [Fact]
    public void NoArguments_PrintsUsageAndExitsOne()
    {
        var (exitCode, output) = Spectral.Tool();

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Help_PrintsUsageAndExitsZero(string flag)
    {
        var (exitCode, output) = Spectral.Tool(flag);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expected 1 argument(s)", "lint")]
    [InlineData("expected 1 argument(s)", "lint", "one.yaml", "two.yaml")]
    [InlineData("expected 1 or 2 argument(s)", "docs")]
    [InlineData("unknown command 'diff'", "diff")]
    [InlineData("no file at no-such-file.yaml", "docs", "no-such-file.yaml")]
    [InlineData("unknown option '--theme'", "docs", "asyncapi.yaml", "--theme", "dark")]
    [InlineData("unknown option '--theme'", "ruleset", "--theme", "dark")]
    public void BadUsage_ExitsTwoAndSaysWhy(string reason, params string[] args)
    {
        var (exitCode, output) = Spectral.Tool(args);

        Assert.Equal(2, exitCode);
        Assert.Contains(reason, output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) Run(string yaml, params string[] options)
    {
        var file = Spectral.Write(yaml);
        try
        {
            return Spectral.Tool(["lint", file, .. options]);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
