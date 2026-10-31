using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Digitals.Tests;

/// <summary>
/// The version check, run through the tool against a fake NuGet registration on localhost. Every tool's test
/// project compiles this file, so each covers VersionCheck.cs on its own. The tool runs an unknown command, which
/// fails with 2 after the check, so nothing else runs.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VersionCheckTests
{
    private static readonly string Package = typeof(VersionCheckTests).Assembly.GetName().Name![..^".Tests".Length];
    private static readonly string Dll = Path.Combine(AppContext.BaseDirectory, $"{Package}.dll");
    private static readonly string Version = FileVersionInfo.GetVersionInfo(Dll).ProductVersion!.Split('+')[0];

    private static readonly string Withdrawn = Index(
        """{ "@id": "https://example.com/page/0.0.1/0.0.9.json" }""",
        Page(
            Entry("0.0.1", """ "deprecation": { "reasons": ["Legacy"] } """),
            Entry(Version, """
                "vulnerabilities": [{ "advisoryUrl": "https://github.com/advisories/GHSA-test", "severity": "2" }],
                "deprecation": { "reasons": ["CriticalBugs"], "message": "Upgrade to 1.0.1." }
                """)));

    [Fact]
    public void Withdrawn_WarnsWithTheAdvisoryAndTheFix()
    {
        using var registry = new Registry(Withdrawn);

        var (exitCode, output) = Run(registry.Url);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            $"version {Version} has a known high vulnerability: https://github.com/advisories/GHSA-test",
            output,
            StringComparison.Ordinal);
        Assert.Contains($"version {Version} is deprecated: Upgrade to 1.0.1.", output, StringComparison.Ordinal);
        Assert.Contains($"upgrade with: dotnet tool update {Package}", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DeprecatedWithoutAMessage_NamesTheReasons()
    {
        using var registry = new Registry(Index(Page(
            Entry(Version, """ "deprecation": { "reasons": ["CriticalBugs", "Legacy"] } """))));

        var (_, output) = Run(registry.Url);

        Assert.Contains($"version {Version} is deprecated: CriticalBugs, Legacy", output, StringComparison.Ordinal);
        Assert.DoesNotContain("vulnerability", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("listed and fine")]
    [InlineData("not listed")]
    public void VersionNothingIsWrongWith_SaysNothing(string listing)
    {
        var entry = listing == "not listed" ? Entry("0.0.1", "") : Entry(Version, "");
        using var registry = new Registry(Index(Page(entry)));

        var (exitCode, output) = Run(registry.Url);

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("upgrade with", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FailOnWithdrawn_StopsTheRun()
    {
        using var registry = new Registry(Withdrawn);

        var (exitCode, output) = Run(registry.Url, ("DT_FAIL_ON_WITHDRAWN", "1"));

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("unknown command", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Off_DoesNotAsk()
    {
        using var registry = new Registry(Withdrawn);

        var (_, output) = Run(registry.Url, ("DT_VERSION_CHECK", "off"));

        Assert.DoesNotContain("upgrade with", output, StringComparison.Ordinal);
    }

    // A registry that fails, answers with something other than JSON, or never answers, which the tool gives up on
    // after two seconds.
    [Theory]
    [InlineData(500, "{}", true)]
    [InlineData(200, "not json", true)]
    [InlineData(200, "{}", false)]
    public void RegistryThatCanNotAnswer_SaysNothing(int status, string body, bool answers)
    {
        using var registry = new Registry(body, (HttpStatusCode)status, answers);

        var (exitCode, output) = Run(registry.Url);

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("upgrade with", output, StringComparison.Ordinal);
    }

    // A day's answer comes from the cache, so the warning stays with the registry gone. A day later it asks again.
    [Fact]
    public void Cache_AnswersForADay()
    {
        string url;
        using (var registry = new Registry(Withdrawn))
        {
            url = registry.Url;
            Run(url);
        }

        Assert.Contains("upgrade with", Run(url).Output, StringComparison.Ordinal);

        File.SetLastWriteTimeUtc(Registry.Cache(url), TimeProvider.System.GetUtcNow().AddDays(-2).UtcDateTime);
        Assert.DoesNotContain("upgrade with", Run(url).Output, StringComparison.Ordinal);
    }

    // Without DT_NUGET_REGISTRATION it asks nuget.org, which doesn't list a build from source.
    [Fact]
    public void NuGetOrg_IsAskedByDefault() =>
        Assert.Equal(2, Run(null).ExitCode);

    private static string Index(params string[] pages) => $$"""{ "items": [{{string.Join(",", pages)}}] }""";

    private static string Page(params string[] entries) =>
        $$"""{ "items": [{{string.Join(",", entries.Select(e => $$"""{ "catalogEntry": {{e}} }"""))}}] }""";

    private static string Entry(string version, string fields) =>
        $$"""{ "version": "{{version}}"{{(fields.Length > 0 ? "," : "")}} {{fields}} }""";

    private static (int ExitCode, string Output) Run(string? registry, params (string Name, string Value)[] settings)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Dll);
        start.ArgumentList.Add("no-such-command");
        start.Environment.Remove("DT_VERSION_CHECK");
        start.Environment.Remove("DT_FAIL_ON_WITHDRAWN");
        start.Environment["DT_NUGET_REGISTRATION"] = registry;
        foreach (var (name, value) in settings)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    /// <summary>A registration on a free localhost port that answers every request the same way, or never.</summary>
    private sealed class Registry : IDisposable
    {
        private readonly HttpListener _listener = new();

        public Registry(string body, HttpStatusCode status = HttpStatusCode.OK, bool answers = true)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}/";
            probe.Stop();

            // A port can come round again, and with it a cached answer from an earlier run.
            Directory.CreateDirectory(Path.GetDirectoryName(Cache(Url))!);
            File.Delete(Cache(Url));
            _listener.Prefixes.Add(Url);
            _listener.Start();
            new Thread(() => Serve(body, status, answers)) { IsBackground = true }.Start();
        }

        public string Url { get; }

        /// <summary>Where the tool caches what <paramref name="url"/> said about this version.</summary>
        public static string Cache(string url)
        {
            var source = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..12];
            return Path.Combine(Path.GetTempPath(), "dt-version-check", $"{Package}-{Version}-{source}.txt");
        }

        public void Dispose() => _listener.Close();

        private void Serve(string body, HttpStatusCode status, bool answers)
        {
            try
            {
                while (true)
                {
                    var context = _listener.GetContext();
                    if (!answers)
                    {
                        continue;
                    }

                    context.Response.StatusCode = (int)status;
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.OutputStream.Write(bytes);
                    context.Response.Close();
                }
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException)
            {
                // Closed, or the tool hung up first.
            }
        }
    }
}
