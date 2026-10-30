using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Digitals.Protobuf;

/// <summary>
/// dt-protobuf: the Digitals rules for .proto files, run through the bundled buf. Each command maps to one
/// call of buf and returns its exit code, so CI fails when it fails.
/// </summary>
internal static class Program
{
    private const string Usage = """
        dt-protobuf: lint, compare and document .proto files against the Digitals rules.

        Usage:
          dt-protobuf lint <input> [options]                Fail when a file breaks a rule
          dt-protobuf format <input> [options]              Fail when a file isn't formatted; --write fixes it
          dt-protobuf breaking <base> <revision> [options]  Fail on a breaking change between two versions
          dt-protobuf docs <input> [<out.html>]             Write the contract as a documentation page
          dt-protobuf ruleset [<out.yaml>] [--baseline]     Print or write the buf config
          dt-protobuf buf <args>                            Run the bundled buf

        <input>, <base> and <revision> are anything buf takes as an input: a directory of .proto files laid
        out by package (acme/notes/v1/notes.proto), a git repo at a ref (.git#branch=main), or an archive.
        lint and breaking take --baseline for the baseline rules: the industry basics, without the Digitals
        house rules. Use it to audit an existing API. They take --config <file> to use another buf config
        instead. Options after the arguments go to buf unchanged, for example --error-format github-actions.
        buf exits 100 when it finds a problem. docs writes one self-contained page, by default <input's
        name>.html in the current directory, that opens offline.
        """;

    private const string RulesetFile = "buf.yaml";
    private const string BaselineFile = "buf-baseline.yaml";

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
            Console.Error.WriteLine($"dt-protobuf {args[0]}: {e.Message}");
            Console.Error.WriteLine("Run dt-protobuf --help for usage.");
            return 2;
        }
    }

    private static readonly Dictionary<string, Func<string[], int>> Commands = new(StringComparer.Ordinal)
    {
        ["lint"] = Lint,
        ["format"] = Format,
        ["breaking"] = Breaking,
        ["docs"] = Docs,
        ["ruleset"] = WriteRuleset,
        ["buf"] = Buf,
    };

    private static int Run(string command, string[] args) => Commands.TryGetValue(command, out var run)
        ? run(args)
        : throw new UsageException($"unknown command '{command}'");

    private static int Lint(string[] args)
    {
        var (input, options) = Split(args, 1, 1);
        return Buf(["lint", input[0], .. WithConfig(options)]);
    }

    // Without --write, buf only reports: --diff prints the change and --exit-code fails on one.
    private static int Format(string[] args)
    {
        var (input, options) = Split(args, 1, 1);
        string[] check = options.Any(o => o is "-w" or "--write") ? [] : ["--diff", "--exit-code"];
        return Buf(["format", input[0], .. check, .. options]);
    }

    // buf compares the input against --against, and each side reads its own config.
    private static int Breaking(string[] args)
    {
        var (inputs, options) = Split(args, 2, 2);
        var withConfig = WithConfig(options);
        string[] againstConfig = withConfig.Contains("--against-config")
            ? []
            : ["--against-config", withConfig[Array.IndexOf(withConfig, "--config") + 1]];
        return Buf(["breaking", inputs[1], "--against", inputs[0], .. againstConfig, .. withConfig]);
    }

    // buf compiles the input and hands it to protoc-gen-doc, whose JSON the page renders. Only the input's own
    // files are documented, not what they import, such as google/protobuf/timestamp.proto.
    private static int Docs(string[] args)
    {
        var (inputs, options) = Split(args, 1, 2);
        if (options.Length > 0)
        {
            throw new UsageException($"unknown option '{options[0]}'");
        }

        var work = Directory.CreateTempSubdirectory("dt-protobuf-");
        try
        {
            var plugin = new { local = Executable("protoc-gen-doc"), @out = work.FullName, opt = "json,contract.json" };
            var template = JsonSerializer.Serialize(new { version = "v2", plugins = new[] { plugin } });
            var exitCode = Buf(["generate", inputs[0], "--template", template]);
            if (exitCode != 0)
            {
                return exitCode;
            }

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputs[0])));
            var json = Path.Combine(work.FullName, "contract.json");
            var output = inputs.ElementAtOrDefault(1) ?? name + ".html";
            ViewerPage.Write("protobuf.html", "protobuf.js", "protobuf.css", json, output, name);
            Console.Error.WriteLine($"Wrote {output}");
            return 0;
        }
        finally
        {
            work.Delete(recursive: true);
        }
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
    /// The options with the config to use: buf.yaml, buf-baseline.yaml for --baseline, or the caller's own
    /// --config. --baseline is ours, so it never reaches buf.
    /// </summary>
    private static string[] WithConfig(string[] options)
    {
        var (baseline, rest) = WithoutBaseline(options);
        if (!rest.Contains("--config"))
        {
            return ["--config", Path.Combine(AppContext.BaseDirectory, baseline ? BaselineFile : RulesetFile), .. rest];
        }

        return baseline ? throw new UsageException("use --baseline or --config, not both") : rest;
    }

    private static (bool Baseline, string[] Others) WithoutBaseline(string[] options) =>
        (options.Contains("--baseline"), [.. options.Where(o => o != "--baseline")]);

    private static int WriteRuleset(string[] args)
    {
        var (paths, options) = Split(args, 0, 1);
        var (baseline, rest) = WithoutBaseline(options);
        if (rest.Length > 0)
        {
            throw new UsageException($"unknown option '{rest[0]}'");
        }

        var source = Path.Combine(AppContext.BaseDirectory, baseline ? BaselineFile : RulesetFile);
        if (paths is not [var path])
        {
            Console.Out.Write(File.ReadAllText(source));
            return 0;
        }

        File.Copy(source, path, overwrite: true);
        Console.Error.WriteLine($"Wrote {path}");
        return 0;
    }

    private static int Buf(string[] args)
    {
        var start = new ProcessStartInfo(Executable("buf")) { UseShellExecute = false };
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
