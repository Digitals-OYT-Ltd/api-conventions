using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Digitals.AsyncApi;

/// <summary>
/// dt-asyncapi: the Digitals ruleset run through the bundled Spectral. Each command maps to one call of
/// Spectral and returns its exit code, so CI fails when it fails.
/// </summary>
internal static class Program
{
    private const string Usage = """
        dt-asyncapi: lint AsyncAPI documents against the Digitals rules.

        Usage:
          dt-asyncapi lint <spec> [options]                 Fail when the document breaks a rule
          dt-asyncapi docs <spec> [<out.html>]              Write the contract as a documentation page
          dt-asyncapi ruleset [<out.yaml>] [--baseline]     Print or write the ruleset, for an editor or CI
          dt-asyncapi spectral <args>                       Run the bundled Spectral

        <spec> is a path or URL to an AsyncAPI 2 or 3 document, JSON or YAML.
        lint and ruleset take --baseline for the baseline ruleset: the industry basics, without the
        Digitals house rules. Use it to audit an existing API. lint takes --ruleset <file> to use another
        ruleset instead. Options after the arguments go to Spectral unchanged, for example
        --format github-actions. Every finding fails lint, whatever its severity. docs writes one
        self-contained page, by default beside the spec with .html, that opens offline.
        """;

    private const string RulesetFile = "digitals.yaml";
    private const string BaselineFile = "digitals-baseline.yaml";
    private const string FunctionsFolder = "functions";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return Run(args[0], args[1..]);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"dt-asyncapi {args[0]}: {e.Message}");
            Console.Error.WriteLine("Run dt-asyncapi --help for usage.");
            return 2;
        }
    }

    private static readonly Dictionary<string, Func<string[], int>> Commands = new(StringComparer.Ordinal)
    {
        ["lint"] = Lint,
        ["docs"] = Docs,
        ["ruleset"] = WriteRuleset,
        ["spectral"] = Spectral,
    };

    private static int Run(string command, string[] args) => Commands.TryGetValue(command, out var run)
        ? run(args)
        : throw new UsageException($"unknown command '{command}'");

    // Spectral fails only on errors by default. The rulesets turn a rule off rather than down, so any
    // finding fails, the same as dt-openapi.
    private static int Lint(string[] args)
    {
        var (spec, options) = Split(args, 1, 1);
        return Spectral(["lint", spec[0], "--fail-severity", "hint", .. WithRuleset(options)]);
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
            "asyncapi.html", "asyncapi-react.js", "asyncapi-react.css", files[0], page, Path.GetFileName(files[0]));
        Console.Error.WriteLine($"Wrote {page}");
        return 0;
    }

    /// <summary>
    /// Splits the leading arguments from the options after them, and checks there are between
    /// <paramref name="min"/> and <paramref name="max"/> arguments.
    /// </summary>
    private static (string[] Arguments, string[] Options) Split(string[] args, int min, int max)
    {
        var arguments = args.TakeWhile(a => !a.StartsWith('-')).ToArray();
        if (arguments.Length < min || arguments.Length > max)
        {
            var expected = min == max ? $"{min}" : $"{min} or {max}";
            throw new UsageException($"expected {expected} argument(s) before any option, got {arguments.Length}");
        }

        return (arguments, args[arguments.Length..]);
    }

    /// <summary>
    /// The options with the ruleset to use: digitals.yaml, digitals-baseline.yaml for --baseline, or the
    /// caller's own --ruleset. --baseline is ours, so it never reaches Spectral.
    /// </summary>
    private static string[] WithRuleset(string[] options)
    {
        var (baseline, rest) = WithoutBaseline(options);
        if (!rest.Any(a => a is "-r" or "--ruleset"))
        {
            var ruleset = Path.Combine(AppContext.BaseDirectory, baseline ? BaselineFile : RulesetFile);
            return ["--ruleset", ruleset, .. rest];
        }

        return baseline ? throw new UsageException("use --baseline or --ruleset, not both") : rest;
    }

    private static (bool Baseline, string[] Others) WithoutBaseline(string[] options) =>
        (options.Contains("--baseline"), [.. options.Where(o => o != "--baseline")]);

    // digitals.yaml extends the baseline by a path relative to itself, so writing it writes the baseline beside
    // it, and the baseline's functions/ beside that.
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

        // The baseline loads its JavaScript rules from functions/ beside it.
        var functions = Directory.CreateDirectory(Path.Combine(folder, FunctionsFolder)).FullName;
        foreach (var function in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, FunctionsFolder), "*.js"))
        {
            File.Copy(function, Path.Combine(functions, Path.GetFileName(function)), overwrite: true);
        }

        Console.Error.WriteLine($"Wrote {functions}");
        return 0;
    }

    private static int Spectral(string[] args)
    {
        var start = new ProcessStartInfo(Executable("spectral")) { UseShellExecute = false };
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
