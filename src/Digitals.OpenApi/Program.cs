using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Digitals.OpenApi;

/// <summary>
/// dt-openapi: the Digitals ruleset run through the vacuum linter, and oasdiff for comparing documents.
/// Each command maps to one call of a bundled binary and returns its exit code, so CI fails when it fails.
/// </summary>
internal static class Program
{
    private const string Usage = """
        dt-openapi: lint and compare OpenAPI documents against the Digitals rules.

        Usage:
          dt-openapi lint <spec> [options]                  Fail when the document breaks a rule
          dt-openapi report <spec> [<out.html>]             Write an HTML report (default openapi-report.html)
          dt-openapi dashboard <spec>                       Browse the findings in the terminal
          dt-openapi docs <spec> [<out.html>]               Write the contract as a Swagger UI page
          dt-openapi diff <design> <implementation>         Fail when the implementation differs from the design
          dt-openapi breaking <base> <revision> [options]   Fail on a breaking change between two versions
          dt-openapi changelog <base> <revision> [options]  List the changes between two versions
          dt-openapi cases <spec> [options]                 List the test cases the contract implies
          dt-openapi ruleset [<out.yaml>] [--baseline]      Print or write the ruleset and its functions folder
          dt-openapi vacuum <args>                          Run the bundled vacuum
          dt-openapi oasdiff <args>                         Run the bundled oasdiff

        <spec> is a path or URL to an OpenAPI 3.0 or 3.1 document, JSON or YAML.
        lint, report, dashboard and ruleset take --baseline for the baseline ruleset: the industry
        basics, without the Digitals house rules. Use it to audit an existing API. lint, report and
        dashboard take --ruleset <file> to use another ruleset instead.
        docs writes one self-contained page, by default beside the spec with .html, that opens offline.
        breaking wants a deprecated operation's x-sunset at least 180 days out.
        cases lists one case per documented response and one per rule on each parameter and request
        body field, each with an ID a test can be named by. --only responses or --only rules lists one
        kind; --format json prints them for an agent.
        Options after the arguments go to vacuum or oasdiff unchanged.
        """;

    // Wording, examples and titles are free to differ: the agent writes its own XML docs. YAML is the
    // only format that also shows tag and component changes. The design's ruleset markers aren't
    // generated, so they don't count either.
    private const string DiffExclusions = "description,examples,summary,title";
    private const string DiffExtensions = "x-dt-unconditional-write,x-dt-no-idempotency-key";

    // The least notice, in days, between deprecating an operation and its x-sunset date. A later
    // --deprecation-days-stable overrides it.
    private const string DeprecationDays = "180";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        if (VersionCheck.Stop("Digitals.OpenApi", "dt-openapi"))
        {
            return 1;
        }

        try
        {
            return Run(args[0], args[1..]);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"dt-openapi {args[0]}: {e.Message}");
            Console.Error.WriteLine("Run dt-openapi --help for usage.");
            return 2;
        }
    }

    private static readonly Dictionary<string, Func<string[], int>> Commands = new(StringComparer.Ordinal)
    {
        ["lint"] = Lint,
        ["report"] = Report,
        ["dashboard"] = Dashboard,
        ["docs"] = Docs,
        ["diff"] = Diff,
        ["breaking"] = Breaking,
        ["changelog"] = Changelog,
        ["cases"] = Cases,
        ["ruleset"] = WriteRuleset,
        ["vacuum"] = Vacuum,
        ["oasdiff"] = Oasdiff,
    };

    private static int Run(string command, string[] args) => Commands.TryGetValue(command, out var run)
        ? run(args)
        : throw new UsageException($"unknown command '{command}'");

    private static int Lint(string[] args)
    {
        var (specs, options) = Split(args, 1);
        // Without --all-results, --details lists only the first 1,000 findings and says nothing about the rest.
        return Vacuum([
            "lint", "--details", "--all-results", "--no-banner", "--no-clip", .. RuleTimeout,
            .. WithAbsolutePaths(specs), .. WithRuleset(options), .. specs,
        ]);
    }

    // A rule that runs past vacuum's timeout, 5 seconds by default, loses its findings and the run still passes.
    // A JavaScript rule on a large contract on a slow CI runner gets there, so allow far longer. A --timeout
    // the caller passes comes later and wins.
    private static readonly string[] RuleTimeout = ["--timeout", "300"];

    // vacuum prints a spec's path relative to the working directory, which for a spec elsewhere is a
    // run of ../ segments. Those print absolute instead.
    private static string[] WithAbsolutePaths(string[] specs) =>
        specs.Select(spec => Path.GetRelativePath(Environment.CurrentDirectory, spec))
            .Any(path => path.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
            ? ["--abs-paths"]
            : [];

    private static int Report(string[] args)
    {
        var (files, options) = Split(args, 1, 2);
        var report = files is [_, var output] ? output : "openapi-report.html";
        return Vacuum(["html-report", .. RuleTimeout, .. WithRuleset(options), files[0], report]);
    }

    private static int Dashboard(string[] args)
    {
        var (spec, options) = Split(args, 1, 1);
        return Vacuum(["dashboard", .. RuleTimeout, .. WithRuleset(options), spec[0]]);
    }

    private static int Docs(string[] args)
    {
        var (files, options) = Split(args, 1, 2);
        if (options.Length > 0)
        {
            throw new UsageException($"unknown option '{options[0]}'");
        }

        if (!File.Exists(files[0]))
        {
            throw new UsageException($"no file at {files[0]}");
        }

        var page = files.ElementAtOrDefault(1) ?? Path.ChangeExtension(files[0], ".html");
        ViewerPage.Write(
            "swagger.html", "swagger-ui-bundle.js", "swagger-ui.css", files[0], page, Path.GetFileName(files[0]));
        Console.Error.WriteLine($"Wrote {page}");
        return 0;
    }

    private static int Diff(string[] args)
    {
        var (specs, options) = Split(args, 2, 2);
        return Oasdiff([
            "diff", .. specs, "--fail-on-diff", "--format", "yaml",
            "--exclude-elements", DiffExclusions, "--exclude-extensions", DiffExtensions, .. options,
        ]);
    }

    // breaking-levels.txt makes a new value in a response enum non-breaking: clients tolerate values
    // they don't know (docs/api-conventions.md). A later --severity-levels replaces it.
    private static int Breaking(string[] args)
    {
        var (specs, options) = Split(args, 2, 2);
        var levels = Path.Combine(AppContext.BaseDirectory, "breaking-levels.txt");
        return Oasdiff([
            "breaking", .. specs, "--fail-on", "ERR",
            "--deprecation-days-stable", DeprecationDays, "--severity-levels", levels, .. options,
        ]);
    }

    private static int Changelog(string[] args)
    {
        var (specs, options) = Split(args, 2, 2);
        return Oasdiff(["changelog", .. specs, .. options]);
    }

    private static int Cases(string[] args)
    {
        var (spec, options) = Split(args, 1, 1);
        var settings = Settings(options, new(StringComparer.Ordinal)
        {
            ["--format"] = ["text", "json"],
            ["--only"] = ["responses", "rules"],
        });
        if (!File.Exists(spec[0]))
        {
            throw new UsageException($"no file at {spec[0]}");
        }

        var (document, errors) = Contract.Read(spec[0]);
        string[] problems = [.. errors, .. document is null ? [] : TestCases.Unnamed(document)];
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"dt-openapi cases: {spec[0]}: {problem}");
        }

        if (document is null)
        {
            return 1;
        }

        var only = settings.GetValueOrDefault("--only");
        var cases = TestCases.From(document, responses: only != "rules", rules: only != "responses");
        var json = settings.GetValueOrDefault("--format") == "json";
        CaseList.Write(cases, TestCaseJson.Default, json, c => $"{c.Method} {c.Path}");
        return 0;
    }

    private static Dictionary<string, string> Settings(string[] options, Dictionary<string, string[]> allowed)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Length; i += 2)
        {
            if (!allowed.TryGetValue(options[i], out var values))
            {
                throw new UsageException($"unknown option '{options[i]}'");
            }

            var value = options.ElementAtOrDefault(i + 1);
            settings[options[i]] = value is not null && values.Contains(value)
                ? value
                : throw new UsageException($"{options[i]} takes {string.Join(" or ", values)}");
        }

        return settings;
    }

    /// <summary>
    /// Splits the leading arguments from the options after them, and checks there are between
    /// <paramref name="min"/> and <paramref name="max"/> arguments.
    /// </summary>
    private static (string[] Arguments, string[] Options) Split(string[] args, int min, int max = int.MaxValue)
    {
        var arguments = args.TakeWhile(a => !a.StartsWith('-')).ToArray();
        if (arguments.Length < min || arguments.Length > max)
        {
            var expected = (min, max) switch
            {
                _ when min == max => $"{min}",
                (_, int.MaxValue) => $"at least {min}",
                _ => $"{min} or {max}",
            };
            throw new UsageException($"expected {expected} argument(s) before any option, got {arguments.Length}");
        }

        return (arguments, args[arguments.Length..]);
    }

    /// <summary>
    /// The options with the ruleset to use: digitals.yaml, digitals-baseline.yaml for --baseline, or the
    /// caller's own --ruleset. --baseline is ours, so it never reaches vacuum. The rules' JavaScript
    /// functions go too, unless the caller passes their own, since a ruleset that extends ours needs them.
    /// </summary>
    private static string[] WithRuleset(string[] options)
    {
        var (baseline, rest) = WithoutBaseline(options);
        string[] functions = rest.Any(a => a is "-f" or "--functions")
            ? []
            : ["--functions", Path.Combine(AppContext.BaseDirectory, FunctionsFolder)];
        if (!rest.Any(a => a is "-r" or "--ruleset"))
        {
            var ruleset = Path.Combine(AppContext.BaseDirectory, baseline ? BaselineFile : RulesetFile);
            return ["--ruleset", ruleset, .. functions, .. rest];
        }

        return baseline ? throw new UsageException("use --baseline or --ruleset, not both") : [.. functions, .. rest];
    }

    private static (bool Baseline, string[] Others) WithoutBaseline(string[] options) =>
        (options.Contains("--baseline"), [.. options.Where(o => o != "--baseline")]);

    private const string RulesetFile = "digitals.yaml";
    private const string BaselineFile = "digitals-baseline.yaml";
    private const string FunctionsFolder = "functions";

    // digitals.yaml extends the baseline by a path relative to itself, so writing it writes the baseline beside
    // it. The functions folder goes beside them too, for vacuum's --functions.
    private static int WriteRuleset(string[] args)
    {
        var (paths, options) = Split(args, 0, 1);
        var (baseline, rest) = WithoutBaseline(options);
        if (rest.Length > 0)
        {
            throw new UsageException($"unknown option '{rest[0]}'");
        }

        var files = baseline ? [BaselineFile] : new[] { RulesetFile, BaselineFile };
        if (paths is not [var path])
        {
            Console.Out.Write(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, files[0])));
            return 0;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var targets = files.Select((file, i) => i == 0 ? path : Path.Combine(folder, file));
        foreach (var (file, target) in files.Zip(targets))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), target, overwrite: true);
            Console.Error.WriteLine($"Wrote {target}");
        }

        var functions = Directory.CreateDirectory(Path.Combine(folder, FunctionsFolder));
        foreach (var function in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, FunctionsFolder), "*.js"))
        {
            File.Copy(function, Path.Combine(functions.FullName, Path.GetFileName(function)), overwrite: true);
        }

        Console.Error.WriteLine($"Wrote {functions.FullName}, which vacuum takes as --functions");
        return 0;
    }

    private static int Vacuum(string[] args) => Start("vacuum", [.. args, "--no-update-check"]);

    private static int Oasdiff(string[] args) => Start("oasdiff", args);

    private static int Start(string tool, string[] args)
    {
        var start = new ProcessStartInfo(Executable(tool)) { UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // Process.Start returns null only when the shell reuses a process, and UseShellExecute is off.
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// The bundled binary. NuGet drops the Unix execute bit when it extracts a package, so the first run sets it.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "The .exe name and the chmod run only on Windows or after NuGet " +
        "extracts the package. Tests run from the build output, where the execute bit is already set.")]
    private static string Executable(string tool)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "native", OperatingSystem.IsWindows() ? tool + ".exe" : tool);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode execute =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & UnixFileMode.UserExecute) == 0)
            {
                File.SetUnixFileMode(path, mode | execute);
            }
        }

        return path;
    }
}

/// <summary>A command line the tool can't run. It prints the reason and the usage hint, and exits 2.</summary>
public sealed class UsageException(string message) : Exception(message);
