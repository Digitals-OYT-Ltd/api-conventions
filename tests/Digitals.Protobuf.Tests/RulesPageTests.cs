using System.Text.RegularExpressions;

namespace Digitals.Protobuf.Tests;

/// <summary>
/// Keeps docs/rules/protobuf.md in step with the configs: every lint rule buf runs at either level has a
/// section, and nothing else does.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class RulesPageTests
{
    private static readonly string Page = File
        .ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "rules", "protobuf.md"))
        .ReplaceLineEndings("\n");

    private static readonly string[] Explained = [.. Headings().Matches(Page).Select(m => m.Groups[1].Value)];

    [Fact]
    public void EveryConfiguredRule_HasASection() =>
        Assert.Equal(Configured("buf-baseline.yaml").Union(Configured("buf.yaml")).Order(), Explained.Order());

    [Fact]
    public void NoRule_IsBothExplainedAndTurnedOff()
    {
        var start = Page.IndexOf("\n## Turned off\n", StringComparison.Ordinal);
        var section = Page[start..Page.IndexOf("\n## ", start + 1, StringComparison.Ordinal)];
        Assert.Empty(Rows().Matches(section).Select(m => m.Groups[1].Value).Intersect(Explained));
    }

    // buf prints a table: a header line, then one rule per line with its id first.
    private static IEnumerable<string> Configured(string config)
    {
        var path = Path.Combine(AppContext.BaseDirectory, config);
        var (exitCode, output) = Buf.Tool("buf", "config", "ls-lint-rules", "--configured-only", "--config", path);
        Assert.Equal(0, exitCode);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split(' ')[0]);
    }

    [GeneratedRegex(@"^### `([^`]+)`$", RegexOptions.Multiline)]
    private static partial Regex Headings();

    [GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex Rows();
}
