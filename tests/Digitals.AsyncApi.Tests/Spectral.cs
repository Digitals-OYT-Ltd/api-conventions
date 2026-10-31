using System.Diagnostics;
using System.Text.Json;

namespace Digitals.AsyncApi.Tests;

/// <summary>Runs dt-asyncapi the same way CI does.</summary>
internal static class Spectral
{
    private static readonly string[] Baseline = ["--baseline"];

    private static string Output => AppContext.BaseDirectory;

    /// <summary>The codes of the rules a document breaks, sorted, one per rule.</summary>
    public static string[] Lint(string yaml, bool baseline = false)
    {
        var file = Write(yaml);
        try
        {
            var (_, output, _) = Run(["lint", file, "--format", "json", .. baseline ? Baseline : []]);

            // The report is a JSON array on stdout, followed by a summary line when it is empty.
            var start = output.IndexOf('[', StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            using var report = JsonDocument.Parse(output[start..(output.LastIndexOf(']') + 1)]);
            return
            [
                .. report.RootElement.EnumerateArray()
                    .Select(result => result.GetProperty("code").GetString()!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static string Write(string yaml)
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, yaml);
        return file;
    }

    /// <summary>Runs dt-asyncapi and returns its exit code and everything it wrote.</summary>
    public static (int ExitCode, string Output) Tool(params string[] args)
    {
        var (exitCode, output, error) = Run(args);
        return (exitCode, output + error);
    }

    // Spectral's Node runtime writes a deprecation warning to stderr on every run, so the report is read from stdout alone.
    private static (int ExitCode, string Output, string Error) Run(string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // The version check has its own tests; the rest stay off the network.
        start.Environment["DT_VERSION_CHECK"] = "off";
        foreach (var arg in (string[])[Path.Combine(Output, "Digitals.AsyncApi.dll"), .. args])
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }
}
