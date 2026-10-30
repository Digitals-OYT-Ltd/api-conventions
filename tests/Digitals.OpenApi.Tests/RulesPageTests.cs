using System.Text.RegularExpressions;

namespace Digitals.OpenApi.Tests;

/// <summary>
/// Keeps docs/rules/openapi.md in step with the rulesets: every rule that is on has a section, and every
/// rule that is off everywhere has a row under "Turned off".
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class RulesPageTests
{
    private static readonly string Page = File
        .ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "rules", "openapi.md"))
        .ReplaceLineEndings("\n");

    private static readonly KeyValuePair<string, bool>[] Named =
        [.. Rules("digitals-baseline.yaml"), .. Rules("digitals.yaml")];

    private static readonly string[] On = [.. Named.Where(r => r.Value).Select(r => r.Key).Distinct()];

    private static readonly string[] OffEverywhere =
        [.. Named.GroupBy(r => r.Key).Where(g => g.All(r => !r.Value)).Select(g => g.Key)];

    [Fact]
    public void EveryRuleThatIsOn_HasASection() =>
        Assert.Equal(On.Order(), Headings().Matches(Page).Select(m => m.Groups[1].Value).Order());

    [Fact]
    public void EveryRuleThatIsOffEverywhere_HasARow() =>
        Assert.Equal(
            OffEverywhere.Order(),
            Rows().Matches(Section("Turned off")).Select(m => m.Groups[1].Value).Order());

    // A key two spaces under rules:. A bare key defines a Digitals rule; a value sets a built-in's severity.
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
