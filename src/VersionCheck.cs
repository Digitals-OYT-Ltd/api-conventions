using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Digitals;

/// <summary>
/// Warns when nuget.org marks the running version deprecated or vulnerable. NuGetAudit doesn't audit
/// <c>dotnet tool restore</c>, so this is how someone on a withdrawn version hears about it. It asks once a day per
/// version, gives up after two seconds, and stays quiet when it can't tell. Shared by every tool.
/// </summary>
internal static class VersionCheck
{
    private const string NuGet = "https://api.nuget.org/v3/registration5-gz-semver2/";

    private static readonly Dictionary<string, string> Severities = new(StringComparer.Ordinal)
    {
        ["0"] = "low",
        ["1"] = "moderate",
        ["2"] = "high",
        ["3"] = "critical",
    };

    /// <summary>
    /// Prints why this version shouldn't be used, if nuget.org says so, and returns whether to stop the run:
    /// only with DT_FAIL_ON_WITHDRAWN=1. DT_VERSION_CHECK=off skips the check, and DT_NUGET_REGISTRATION points it
    /// at a mirror's registration base instead of nuget.org's.
    /// </summary>
    public static bool Stop(string package, string command)
    {
        if (Environment.GetEnvironmentVariable("DT_VERSION_CHECK") == "off")
        {
            return false;
        }

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var registration = Environment.GetEnvironmentVariable("DT_NUGET_REGISTRATION") ?? NuGet;
        var warnings = Cached(package, version, registration);
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"{command}: {warning}");
        }

        return warnings.Length > 0 && Environment.GetEnvironmentVariable("DT_FAIL_ON_WITHDRAWN") == "1";
    }

    // A day's answer per version and registration, so a run doesn't wait on the network each time.
    private static string[] Cached(string package, string version, string registration)
    {
        var source = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(registration)))[..12];
        var cache = Path.Combine(Path.GetTempPath(), "dt-version-check", $"{package}-{version}-{source}.txt");
        var age = TimeProvider.System.GetUtcNow() - File.GetLastWriteTimeUtc(cache);
        if (File.Exists(cache) && age < TimeSpan.FromDays(1))
        {
            return File.ReadAllLines(cache);
        }

        var warnings = Ask(package, version, registration);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllLines(cache, warnings);
        return warnings;
    }

    // The registration index inlines each version's catalog entry, with its deprecation and vulnerabilities.
    // Anything that goes wrong, from no network to a page nuget.org didn't inline, means nothing to say.
    private static string[] Ask(string package, string version, string registration)
    {
        try
        {
            using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{registration}{package.ToLowerInvariant()}/index.json");
            using var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            using var body = response.Content.ReadAsStream();
            var entry = JsonNode.Parse(body)!["items"]!.AsArray()
                .SelectMany(page => page!["items"] is JsonArray leaves ? leaves : [])
                .Select(leaf => leaf!["catalogEntry"]!)
                .FirstOrDefault(e => string.Equals(
                    e["version"]!.GetValue<string>(), version, StringComparison.OrdinalIgnoreCase));
            string[] warnings = entry is null
                ? []
                : [.. Vulnerabilities(entry, version), .. Deprecation(entry, version)];
            return warnings.Length == 0 ? [] : [.. warnings, $"upgrade with: dotnet tool update {package}"];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IEnumerable<string> Vulnerabilities(JsonNode entry, string version) =>
        entry["vulnerabilities"] is JsonArray found
            ? found.Select(v => $"version {version} has a known " +
                $"{Severities.GetValueOrDefault(v!["severity"]!.GetValue<string>(), "unrated")} vulnerability: " +
                $"{v["advisoryUrl"]!.GetValue<string>()}")
            : [];

    private static IEnumerable<string> Deprecation(JsonNode entry, string version) =>
        entry["deprecation"] is JsonObject deprecation
            ? [$"version {version} is deprecated: {Reason(deprecation)}"]
            : [];

    // The publisher's message, or nuget.org's reason codes such as CriticalBugs when there's none.
    private static string Reason(JsonObject deprecation) =>
        deprecation["message"] is JsonValue message
            ? message.GetValue<string>()
            : string.Join(", ", deprecation["reasons"]!.AsArray().Select(r => r!.GetValue<string>()));
}
