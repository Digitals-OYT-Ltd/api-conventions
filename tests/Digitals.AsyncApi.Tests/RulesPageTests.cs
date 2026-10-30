using System.Text.RegularExpressions;

namespace Digitals.AsyncApi.Tests;

/// <summary>
/// Keeps docs/rules/asyncapi.md in step with the rulesets. They name only the Digitals rules and the Spectral
/// rules they change, so every named rule that is on needs a section and every one off everywhere a row
/// under "Turned off".
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class RulesPageTests
{
    private static readonly string Page = File
        .ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "rules", "asyncapi.md"))
        .ReplaceLineEndings("\n");

    private static readonly KeyValuePair<string, bool>[] Named =
        [.. Rules("digitals-baseline.yaml"), .. Rules("digitals.yaml")];

    private static readonly string[] On = [.. Named.Where(r => r.Value).Select(r => r.Key).Distinct()];

    private static readonly string[] OffEverywhere =
        [.. Named.GroupBy(r => r.Key).Where(g => g.All(r => !r.Value)).Select(g => g.Key)];

    private static readonly string[] Explained = [.. Headings().Matches(Page).Select(m => m.Groups[1].Value)];

    private static readonly string[] TurnedOff =
        [.. Rows().Matches(Section("Turned off")).Select(m => m.Groups[1].Value)];

    [Fact]
    public void EveryNamedRuleThatIsOn_HasASection() =>
        Assert.Empty(On.Except(Explained));

    [Fact]
    public void EveryNamedRuleThatIsOffEverywhere_HasARow() =>
        Assert.Empty(OffEverywhere.Except(TurnedOff));

    [Fact]
    public void NoRule_IsBothExplainedAndTurnedOff() =>
        Assert.Empty(Explained.Intersect(TurnedOff));

    // A key two spaces under rules:. A bare key defines a Digitals rule; a value sets a Spectral rule's severity.
    private static Dictionary<string, bool> Rules(string ruleset) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, ruleset))
            .SkipWhile(line => line != "rules:")
            .Select(line => RuleKey().Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value != "off");

    private static string Section(string heading)
    {
        var start = Page.IndexOf($"\n## {heading}\n", StringComparison.Ordinal);
        var end = Page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return Page[start..end];
    }

    [GeneratedRegex(@"^  ([\w$-]+):[ ]*([^\s#]*)")]
    private static partial Regex RuleKey();

    [GeneratedRegex(@"^### `([^`]+)`$", RegexOptions.Multiline)]
    private static partial Regex Headings();

    [GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex Rows();
}
