namespace Digitals.Protobuf.Tests;

/// <summary>The commands CI runs, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CommandTests
{
    [Fact]
    public void Lint_CleanModule_ExitsZero() =>
        Assert.Equal(0, Run("lint", RulesTests.Clean).ExitCode);

    [Fact]
    public void Lint_BrokenRule_Exits100AndExplains()
    {
        var (exitCode, output) = Run(
            "lint",
            RulesTests.Clean.Replace("service NoteService", "service Notes", StringComparison.Ordinal));

        Assert.Equal(100, exitCode);
        // buf names the rule only in --error-format json; the text says what to change.
        Assert.Contains("should be suffixed with \"Service\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_BaselineAndConfig_IsAUsageError() =>
        Assert.Equal(2, Run("lint", RulesTests.Clean, "--baseline", "--config", "buf.yaml").ExitCode);

    [Fact]
    public void Format_FormattedModule_ExitsZero() =>
        Assert.Equal(0, Run("format", RulesTests.Clean).ExitCode);

    [Fact]
    public void Format_UnformattedModule_PrintsTheDiff()
    {
        var (exitCode, output) = Run(
            "format",
            RulesTests.Clean.Replace("  string title = 2;", "string   title=2;", StringComparison.Ordinal));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("string title = 2;", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Ruleset_Baseline_PrintsOnlyTheBasics()
    {
        var (exitCode, output) = Buf.Tool("ruleset", "--baseline");

        Assert.Equal(0, exitCode);
        Assert.Contains("- BASIC", output, StringComparison.Ordinal);
        Assert.DoesNotContain("COMMENTS", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Ruleset_WrittenConfig_LintsTheSame()
    {
        var config = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        try
        {
            Assert.Equal(0, Buf.Tool("ruleset", config).ExitCode);
            Assert.Equal(100, Run(
                "lint",
                RulesTests.Clean.Replace("service NoteService", "service Notes", StringComparison.Ordinal),
                "--config",
                config).ExitCode);
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public void Docs_WritesAPageThatNeedsNoNetwork()
    {
        var page = Docs(RulesTests.Clean, out var exitCode);

        Assert.Equal(0, exitCode);
        Assert.Contains("A note as the service stores it.", page, StringComparison.Ordinal);
        Assert.Contains("<title>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%SPEC%", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<link", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_ScriptInTheContract_StaysText()
    {
        var page = Docs(RulesTests.Clean.Replace(
            "A note as the service stores it.",
            "</script><img src=x onerror=alert(1)>",
            StringComparison.Ordinal), out var exitCode);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("</script><img", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_FileThatDoesNotCompile_FailsWithoutAPage()
    {
        var page = Docs(
            RulesTests.Clean.Replace("message Note {", "message Note", StringComparison.Ordinal),
            out var exitCode);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(page);
    }

    [Fact]
    public void Docs_UnknownOption_IsAUsageError() =>
        Assert.Equal(2, Run("docs", RulesTests.Clean, "--theme", "dark").ExitCode);

    // The default page is named after the input and lands in the working directory, which the tool
    // inherits from the test run.
    [Fact]
    public void Docs_NoOutput_WritesTheDefaultPage()
    {
        var module = Buf.Module(RulesTests.Clean);
        var page = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(module) + ".html");
        try
        {
            Assert.Equal(0, Buf.Tool("docs", module).ExitCode);
            Assert.Contains("A note as the service stores it.", File.ReadAllText(page), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
            File.Delete(page);
        }
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("--write")]
    public void Format_Write_FixesTheFile(string flag)
    {
        var module = Buf.Module(
            RulesTests.Clean.Replace("  string title = 2;", "string   title=2;", StringComparison.Ordinal));
        try
        {
            Assert.Equal(0, Buf.Tool("format", module, flag).ExitCode);
            Assert.Equal(0, Buf.Tool("format", module).ExitCode);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }

    [Fact]
    public void Breaking_OwnAgainstConfig_ExitsZero()
    {
        var module = Buf.Module(RulesTests.Clean);
        var config = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        try
        {
            Assert.Equal(0, Buf.Tool("ruleset", config).ExitCode);
            Assert.Equal(0, Buf.Tool("breaking", module, module, "--against-config", config).ExitCode);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
            File.Delete(config);
        }
    }

    [Fact]
    public void Buf_RunsTheBundledTool() =>
        Assert.Equal(0, Buf.Tool("buf", "--version").ExitCode);

    [Fact]
    public void NoArguments_PrintsUsageAndExitsOne()
    {
        var (exitCode, output) = Buf.Tool();

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Help_PrintsUsageAndExitsZero(string flag)
    {
        var (exitCode, output) = Buf.Tool(flag);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expected 1 argument(s)", "lint")]
    [InlineData("expected 1 argument(s)", "lint", "one", "two")]
    [InlineData("expected 1 or 2 argument(s)", "docs")]
    [InlineData("unknown command 'vet'", "vet")]
    [InlineData("unknown option '--theme'", "ruleset", "--theme", "dark")]
    public void BadUsage_ExitsTwoAndSaysWhy(string reason, params string[] args)
    {
        var (exitCode, output) = Buf.Tool(args);

        Assert.Equal(2, exitCode);
        Assert.Contains(reason, output, StringComparison.Ordinal);
    }

    /// <summary>Runs docs on a module holding <paramref name="proto"/> and returns the page, or "" when none was written.</summary>
    private static string Docs(string proto, out int exitCode)
    {
        var module = Buf.Module(proto);
        var page = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            exitCode = Buf.Tool("docs", module, page).ExitCode;
            return File.Exists(page) ? File.ReadAllText(page) : "";
        }
        finally
        {
            Directory.Delete(module, recursive: true);
            File.Delete(page);
        }
    }

    private static (int ExitCode, string Output) Run(string command, string proto, params string[] options)
    {
        var module = Buf.Module(proto);
        try
        {
            return Buf.Tool([command, module, .. options]);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }
}
