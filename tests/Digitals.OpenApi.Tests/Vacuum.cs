using System.Diagnostics;
using System.Text.Json;

namespace Digitals.OpenApi.Tests;

/// <summary>Runs the bundled vacuum and the tool the same way CI does.</summary>
internal static class Vacuum
{
    private static string Output => AppContext.BaseDirectory;

    /// <summary>The codes of the rules a document breaks, sorted, one per rule.</summary>
    public static string[] Lint(string yaml, string ruleset = "digitals.yaml")
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, yaml);
        try
        {
            var (_, output) = Run(
                Path.Combine(Output, "native", OperatingSystem.IsWindows() ? "vacuum.exe" : "vacuum"),
                ["spectral-report", "--ruleset", Path.Combine(Output, ruleset), "--functions", Path.Combine(
                    Output,
                    "functions"), "--no-style", "--stdout", "--no-update-check", file]);

            // The report is the JSON array after vacuum's banner.
            using var report = JsonDocument.Parse(output[output.IndexOf('[', StringComparison.Ordinal)..]);
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

    /// <summary>Runs dt-openapi and returns its exit code and everything it wrote.</summary>
    public static (int ExitCode, string Output) Tool(params string[] args) =>
        Run(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [Path.Combine(Output, "Digitals.OpenApi.dll"), .. args]);

    private static (int ExitCode, string Output) Run(string executable, string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // The version check has its own tests; the rest stay off the network.
        start.Environment["DT_VERSION_CHECK"] = "off";
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }
}
