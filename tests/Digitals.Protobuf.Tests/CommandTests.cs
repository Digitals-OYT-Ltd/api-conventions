using System.IO.Compression;

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

    // A contract that imports protovalidate without its own copy, so the tool brings the bundled one.
    private static readonly string Validated = RulesTests.Clean
        .Replace(
            "import \"google",
            "import \"buf/validate/validate.proto\";\nimport \"google",
            StringComparison.Ordinal)
        .Replace("  string title = 2;", "  string title = 2 [(buf.validate.field).string.max_len = 80];",
            StringComparison.Ordinal);

    [Fact]
    public void Lint_ImportsProtovalidate_ExitsZero() =>
        Assert.Equal(0, Run("lint", Validated).ExitCode);

    [Fact]
    public void Lint_ImportsProtovalidate_NamesTheInputsFile()
    {
        var module = Buf.Module(Validated.Replace("service NoteService", "service Notes", StringComparison.Ordinal));
        var file = $"{module}/acme/notes/v1/notes.proto";
        try
        {
            var (exitCode, output) = Buf.Tool("lint", module, "--path", file, "--error-format", "json");

            Assert.Equal(100, exitCode);
            Assert.Contains($"\"path\":\"{file}\"", output, StringComparison.Ordinal);
            Assert.DoesNotContain("validate.proto", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }

    [Fact]
    public void Format_ImportsProtovalidate_ExitsZero() =>
        Assert.Equal(0, Run("format", Validated).ExitCode);

    [Fact]
    public void Docs_ImportsProtovalidate_DocumentsOnlyTheContract()
    {
        var page = Docs(Validated, out var exitCode);

        Assert.Equal(0, exitCode);
        Assert.Contains("A note as the service stores it.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FieldRules", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Breaking_ImportsProtovalidate_FindsTheChange()
    {
        var (baseModule, revision) = Revised();
        try
        {
            var (exitCode, output) = Buf.Tool("breaking", baseModule, revision);

            Assert.Equal(100, exitCode);
            Assert.Contains($"{revision}/acme/notes/v1/notes.proto:", output, StringComparison.Ordinal);
            Assert.Contains("changed type", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(baseModule, recursive: true);
            Directory.Delete(revision, recursive: true);
        }
    }

    // buf format writes out an input that isn't a directory, here an archive, so it gets protovalidate too.
    [Fact]
    public void Breaking_ArchiveBase_FindsTheChange()
    {
        var (baseModule, revision) = Revised();
        var archive = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(baseModule, archive);
            var (exitCode, output) = Buf.Tool("breaking", archive, revision);

            Assert.Equal(100, exitCode);
            Assert.Contains("changed type", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(baseModule, recursive: true);
            Directory.Delete(revision, recursive: true);
            File.Delete(archive);
        }
    }

    [Fact]
    public void Breaking_BaseThatIsNotThere_FailsAsBufSays()
    {
        var revision = Buf.Module(Validated);
        try
        {
            var (exitCode, output) = Buf.Tool("breaking", $"{revision}.zip", revision);

            Assert.Equal(1, exitCode);
            Assert.Contains($"{revision}.zip", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(revision, recursive: true);
        }
    }

    // A side that brings its own copy keeps it; the other side gets the bundled one, the same file.
    [Fact]
    public void Breaking_BaseWithItsOwnProtovalidate_KeepsIt()
    {
        var (baseModule, revision) = (Buf.Module(Validated), Buf.Module(Validated));
        try
        {
            var own = Path.Combine(baseModule, "buf", "validate");
            Directory.CreateDirectory(own);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "native", "validate.proto"),
                Path.Combine(own, "validate.proto"));

            Assert.Equal(0, Buf.Tool("breaking", baseModule, revision).ExitCode);
        }
        finally
        {
            Directory.Delete(baseModule, recursive: true);
            Directory.Delete(revision, recursive: true);
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

    /// <summary>A module importing protovalidate, and a revision of it that changes a field's type.</summary>
    private static (string Base, string Revision) Revised() => (
        Buf.Module(Validated),
        Buf.Module(Validated.Replace("NoteState state = 4;", "string state = 4;", StringComparison.Ordinal)));

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
