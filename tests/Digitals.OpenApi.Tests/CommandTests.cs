using System.Globalization;

namespace Digitals.OpenApi.Tests;

/// <summary>The commands CI runs, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CommandTests
{
    private const string Design = """
        openapi: 3.1.1
        info: { title: Notes, version: v1, description: Short text notes kept by the service. }
        tags: [{ name: Notes, description: Reading notes by id. }]
        security: [{ bearer: [] }]
        paths:
          /api/v1/notes/{noteId}:
            get:
              tags: [Notes]
              summary: Gets a note.
              description: Returns the note with the given id.
              operationId: GetNote
              parameters:
                - { name: noteId, in: path, description: The note's id., required: true, example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: { type: string, format: uuid, maxLength: 36 } }
              responses:
                '200': { description: OK, headers: { RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' } }, content: { application/json: { schema: { $ref: '#/components/schemas/Note' } } } }
                '401': { description: Unauthorized, headers: { RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' } }, content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' }, example: { title: unauthorized, status: 401 } } } }
                '429': { description: Too many requests., headers: { RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' }, Retry-After: { $ref: '#/components/headers/Retry-After' } }, content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' }, example: { title: toomany, status: 429 } } } }
                '500': { description: The service failed., content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' }, example: { title: failed, status: 500 } } } }
        components:
          headers:
            RateLimit-Policy: { description: The caller's quota of q requests per window of w seconds., schema: { type: string, maxLength: 128, pattern: '^"[a-z0-9-]+";q=\d+;w=\d+$' }, example: '"standard";q=100;w=60' }
            RateLimit: { description: Requests left in this window (r) and seconds until it resets (t)., schema: { type: string, maxLength: 128, pattern: '^"[a-z0-9-]+";r=\d+;t=\d+$' }, example: '"standard";r=42;t=17' }
            Retry-After: { description: Seconds to wait before trying again., schema: { type: integer, format: int32, minimum: 0, maximum: 86400 }, example: 17 }
          securitySchemes:
            bearer: { type: http, scheme: bearer, bearerFormat: JWT, description: An access token (JWT) from the identity provider. }
          schemas:
            Note: { type: object, description: A note as the service stores it., example: { title: groceries }, properties: { title: { type: string, maxLength: 200, pattern: '^[a-z]*$', description: The note's text on one line. } } }
            Problem: { type: object, description: An error response in RFC 9457 problem details format., example: { title: unauthorized }, properties: { title: { type: string, maxLength: 200, pattern: '^[a-z]*$', description: A short summary of the kind of error. } } }
        """;

    [Fact]
    public void Lint_CleanDocument_ExitsZero() =>
        Assert.Equal(0, Vacuum.Tool("lint", Write(Design)).ExitCode);

    [Fact]
    public void Lint_BrokenRule_ExitsOneAndNamesIt()
    {
        var (exitCode, output) = Vacuum.Tool(
            "lint",
            Write(Design.Replace("GetNote", "getNote", StringComparison.Ordinal)));

        Assert.Equal(1, exitCode);
        Assert.Contains("dt-operation-id-casing", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_FieldNamedLikeADate_NamesTheField()
    {
        var (_, output) = Vacuum.Tool("lint", Write(WithNoteProperty(
            "dueDate: { type: string, maxLength: 10, pattern: '^[0-9-]*$', description: When it's due. }")));

        Assert.Contains("'dueDate' is named like a calendar date", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_OverAThousandFindings_ListsEveryOne()
    {
        var properties = string.Join(", ", Enumerable.Range(0, 1100).Select(i => $"flag{i}: {{ type: boolean }}"));

        var (_, output) = Vacuum.Tool("lint", Write(WithNoteProperty(properties)));

        Assert.Equal(1100, output.Split("rule: dt-description").Length - 1);
    }

    [Fact]
    public void Lint_SpecOutsideWorkingDirectory_PrintsAbsolutePath()
    {
        var file = Write(Design.Replace("GetNote", "getNote", StringComparison.Ordinal));

        Assert.Contains(Environment.NewLine + file + ":", Vacuum.Tool("lint", file).Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_SpecInWorkingDirectory_PrintsRelativePath()
    {
        var name = $"{Guid.NewGuid():N}.yaml";
        File.WriteAllText(name, Design.Replace("GetNote", "getNote", StringComparison.Ordinal));
        try
        {
            var output = Vacuum.Tool("lint", name).Output;

            Assert.Contains(Environment.NewLine + name + ":", output, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFullPath(name), output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(name);
        }
    }

    [Fact]
    public void Diff_WordingOnly_ExitsZero()
    {
        var implementation = Design.Replace("Gets a note.", "Returns the note.", StringComparison.Ordinal);

        Assert.Equal(0, Vacuum.Tool("diff", Write(Design), Write(implementation)).ExitCode);
    }

    [Fact]
    public void Diff_ContractChange_ExitsOneAndShowsIt()
    {
        var (exitCode, output) = Vacuum.Tool(
            "diff",
            Write(Design),
            Write(Design.Replace("maxLength: 200", "maxLength: 100", StringComparison.Ordinal)));

        Assert.Equal(1, exitCode);
        Assert.Contains("maxLength:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_RulesetMarker_ExitsZero()
    {
        var design = Design.Replace(
            "operationId: GetNote\n",
            "operationId: GetNote\n      x-dt-unconditional-write: Only the owner writes it.\n      x-dt-no-idempotency-key: A repeat is harmless.\n",
            StringComparison.Ordinal);

        Assert.NotEqual(Design, design);
        Assert.Equal(0, Vacuum.Tool("diff", Write(design), Write(Design)).ExitCode);
    }

    [Fact]
    public void Breaking_RemovedOperation_ExitsOne()
    {
        var revision = Design.Replace(
            "/api/v1/notes/{noteId}:",
            "/api/v1/notes/{noteId}:archive:",
            StringComparison.Ordinal);

        Assert.Equal(1, Vacuum.Tool("breaking", Write(Design), Write(revision)).ExitCode);
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(200, 0)]
    public void Breaking_Deprecation_NeedsSixMonthsNotice(int days, int expected)
    {
        var sunset = TimeProvider.System.GetUtcNow().AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var revision = Design.Replace(
            "operationId: GetNote\n",
            $"operationId: GetNote\n      deprecated: true\n      x-sunset: '{sunset}'\n",
            StringComparison.Ordinal);

        Assert.Equal(expected, Vacuum.Tool("breaking", Write(Design), Write(revision)).ExitCode);
    }

    [Fact]
    public void Breaking_LaterDeprecationDays_Overrides()
    {
        var sunset = TimeProvider.System.GetUtcNow().AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var revision = Design.Replace(
            "operationId: GetNote\n",
            $"operationId: GetNote\n      deprecated: true\n      x-sunset: '{sunset}'\n",
            StringComparison.Ordinal);

        Assert.Equal(
            0,
            Vacuum.Tool("breaking", Write(Design), Write(revision), "--deprecation-days-stable", "7").ExitCode);
    }

    [Fact]
    public void Breaking_RemovedAfterSunset_ExitsZero()
    {
        var deprecated = Design.Replace(
            "operationId: GetNote\n",
            "operationId: GetNote\n      deprecated: true\n      x-sunset: '2020-01-01'\n",
            StringComparison.Ordinal);
        var revision = Design.Replace(
            "/api/v1/notes/{noteId}:",
            "/api/v1/notes/{noteId}:archive:",
            StringComparison.Ordinal);

        Assert.Equal(0, Vacuum.Tool("breaking", Write(deprecated), Write(revision)).ExitCode);
    }

    // Clients tolerate enum values they don't know, so a response enum may grow or shrink.
    [Theory]
    [InlineData("[active]", "[active, archived]")]
    [InlineData("[active, archived]", "[active]")]
    public void Breaking_ResponseEnumChange_ExitsZero(string before, string after) =>
        Assert.Equal(0, Vacuum.Tool("breaking", Write(WithNoteStatus(before)), Write(WithNoteStatus(after))).ExitCode);

    // vacuum drops the OWASP rules of a ruleset extended by file unless the extending one lists them again.
    [Fact]
    public void Lint_ExtendingRuleset_KeepsEveryOtherRule()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        Assert.Equal(0, Vacuum.Tool("ruleset", Path.Combine(folder, "digitals.yaml")).ExitCode);
        var ruleset = Path.Combine(folder, "mine.yaml");
        File.WriteAllText(ruleset, """
            extends:
              - ./digitals.yaml
              - [spectral:owasp, all]
            rules:
              owasp-no-credentials-in-url: off
            """);
        var pageToken = Design.Replace(
            "      parameters:\n",
            "      parameters:\n        - { name: pageToken, in: query, description: d., example: abc, schema: { type: string, maxLength: 20, pattern: '^[a-z]*$' } }\n",
            StringComparison.Ordinal);

        Assert.Equal(0, Vacuum.Tool("lint", Write(pageToken), "--ruleset", ruleset).ExitCode);
        var (exitCode, output) = Vacuum.Tool(
            "lint",
            Write(pageToken.Replace("maxLength: 20, ", "", StringComparison.Ordinal)),
            "--ruleset",
            ruleset);
        Assert.Equal(1, exitCode);
        Assert.Contains("dt-string-limit", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Ruleset_PrintsTheRuleset() =>
        Assert.Contains("dt-path-version:", Vacuum.Tool("ruleset").Output, StringComparison.Ordinal);

    [Fact]
    public void Ruleset_Baseline_PrintsOnlyTheBasics()
    {
        var output = Vacuum.Tool("ruleset", "--baseline").Output;
        Assert.Contains("dt-no-personal-data-in-url:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dt-path-version:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_BaselineSkipsHouseStyle_ExitsZero()
    {
        var unversioned = Write(Design.Replace("/api/v1/notes", "/api/notes", StringComparison.Ordinal));
        Assert.Equal(1, Vacuum.Tool("lint", unversioned).ExitCode);
        Assert.Equal(0, Vacuum.Tool("lint", unversioned, "--baseline").ExitCode);
    }

    [Fact]
    public void Lint_BaselineAndRuleset_IsAUsageError() =>
        Assert.Equal(2, Vacuum.Tool("lint", Write(Design), "--baseline", "--ruleset", "mine.yaml").ExitCode);

    [Fact]
    public void Swagger_WritesAPageThatNeedsNoNetwork()
    {
        var spec = Write(Design);
        var page = Path.ChangeExtension(spec, ".html");
        try
        {
            Assert.Equal(0, Vacuum.Tool("swagger", spec).ExitCode);

            var html = File.ReadAllText(page);
            Assert.Contains("SwaggerUIBundle(", html, StringComparison.Ordinal);
            // Swagger UI fetches the contract again to resolve each $ref, and the page answers that from the inlined text.
            Assert.Contains("https://contract.invalid/openapi", html, StringComparison.Ordinal);
            Assert.Contains("A note as the service stores it.", html, StringComparison.Ordinal);
            Assert.DoesNotContain("%SPEC%", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script src", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<link", html, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(page);
        }
    }

    [Fact]
    public void Swagger_ScriptInTheContract_StaysText()
    {
        var page = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            var spec = Write(Design.Replace(
                "A note as the service stores it.",
                "'</script><img src=x onerror=alert(1)>'",
                StringComparison.Ordinal));
            Assert.Equal(0, Vacuum.Tool("swagger", spec, page).ExitCode);
            Assert.DoesNotContain("</script><img", File.ReadAllText(page), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(page);
        }
    }

    [Fact]
    public void Report_WritesTheReport()
    {
        var report = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            Assert.Equal(0, Vacuum.Tool("report", Write(Design), report).ExitCode);
            Assert.True(File.Exists(report));
        }
        finally
        {
            File.Delete(report);
        }
    }

    // The default report lands in the working directory, which the tool inherits from the test run.
    [Fact]
    public void Report_NoOutput_WritesTheDefaultFile()
    {
        var report = Path.Combine(Environment.CurrentDirectory, "openapi-report.html");
        try
        {
            Assert.Equal(0, Vacuum.Tool("report", Write(Design)).ExitCode);
            Assert.True(File.Exists(report));
        }
        finally
        {
            File.Delete(report);
        }
    }

    // The dashboard is interactive, so only its failure to load a file can run here.
    [Fact]
    public void Dashboard_MissingSpec_FailsInVacuum()
    {
        var (exitCode, output) = Vacuum.Tool("dashboard", "no-such-file.yaml");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("failed to read file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_ListsTheChange()
    {
        var (exitCode, output) = Vacuum.Tool(
            "changelog", Write(Design), Write(WithNoteProperty("pinned: { type: boolean, description: d. }")));

        Assert.Equal(0, exitCode);
        Assert.Contains("pinned", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Lint_OwnFunctions_KeepsOurRuleset()
    {
        var functions = Path.Combine(AppContext.BaseDirectory, "functions");
        Assert.Equal(0, Vacuum.Tool("lint", Write(Design), "--functions", functions).ExitCode);
    }

    [Theory]
    [InlineData("vacuum", "version")]
    [InlineData("oasdiff", "--version")]
    public void Passthrough_RunsTheBundledTool(params string[] args) =>
        Assert.Equal(0, Vacuum.Tool(args).ExitCode);

    [Fact]
    public void NoArguments_PrintsUsageAndExitsOne()
    {
        var (exitCode, output) = Vacuum.Tool();

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Help_PrintsUsageAndExitsZero(string flag)
    {
        var (exitCode, output) = Vacuum.Tool(flag);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expected 2 argument(s)", "diff", "one.yaml")]
    [InlineData("expected at least 1 argument(s)", "lint")]
    [InlineData("expected 1 or 2 argument(s)", "report")]
    [InlineData("unknown command 'unknown'", "unknown")]
    [InlineData("no file at no-such-file.yaml", "swagger", "no-such-file.yaml")]
    [InlineData("unknown option '--theme'", "swagger", "openapi.yaml", "--theme", "dark")]
    [InlineData("unknown option '--theme'", "ruleset", "--theme", "dark")]
    public void BadUsage_ExitsTwoAndSaysWhy(string reason, params string[] args)
    {
        var (exitCode, output) = Vacuum.Tool(args);

        Assert.Equal(2, exitCode);
        Assert.Contains(reason, output, StringComparison.Ordinal);
    }

    private static string WithNoteStatus(string values) =>
        WithNoteProperty(
            $"status: {{ type: string, enum: {values}, description: Where the note is in its lifecycle. }}");

    private static string WithNoteProperty(string property) => Design.Replace(
        "description: A note as the service stores it., example: { title: groceries }, properties: {",
        $"description: A note as the service stores it., example: {{ title: groceries }}, properties: {{ {property},",
        StringComparison.Ordinal);

    private static string Write(string yaml)
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, yaml);
        return file;
    }
}
