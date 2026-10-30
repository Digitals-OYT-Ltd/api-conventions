using System.Diagnostics;
using System.Text.Json;

namespace Digitals.Protobuf.Tests;

/// <summary>Runs dt-protobuf on .proto files written to a throwaway module.</summary>
internal static class Buf
{
    private static readonly string[] Baseline = ["--baseline"];

    private static string Output => AppContext.BaseDirectory;

    /// <summary>
    /// The rules a file breaks, sorted, one per rule. The file goes in acme/notes/v1/, the directory its
    /// package names, unless <paramref name="directory"/> says otherwise.
    /// </summary>
    public static string[] Lint(string proto, bool baseline = false, string directory = "acme/notes/v1")
    {
        var module = Module(proto, directory);
        try
        {
            var (_, output) = Tool(["lint", module, "--error-format", "json", .. baseline ? Baseline : []]);
            return Rules(output);
        }
        finally
        {
            Directory.Delete(module, recursive: true);
        }
    }

    /// <summary>The breaking-change rules a revision of a file breaks, sorted, one per rule.</summary>
    public static string[] Breaking(string baseProto, string revisionProto)
    {
        var (baseModule, revisionModule) = (Module(baseProto), Module(revisionProto));
        try
        {
            var (_, output) = Tool("breaking", baseModule, revisionModule, "--error-format", "json");
            return Rules(output);
        }
        finally
        {
            Directory.Delete(baseModule, recursive: true);
            Directory.Delete(revisionModule, recursive: true);
        }
    }

    /// <summary>A module holding one file, notes.proto, in <paramref name="directory"/>.</summary>
    public static string Module(string proto, string directory = "acme/notes/v1")
    {
        var module = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(module, directory);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.proto"), proto);
        return module;
    }

    /// <summary>Runs dt-protobuf and returns its exit code and everything it wrote.</summary>
    public static (int ExitCode, string Output) Tool(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in (string[])[Path.Combine(Output, "Digitals.Protobuf.dll"), .. args])
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    // buf's JSON format is one object per line, with the rule in "type".
    private static string[] Rules(string output) =>
    [
        .. output.Split('\n')
            .Where(line => line.StartsWith('{'))
            .Select(line =>
            {
                using var finding = JsonDocument.Parse(line);
                return finding.RootElement.GetProperty("type").GetString()!;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}
