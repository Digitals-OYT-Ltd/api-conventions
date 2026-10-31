using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Digitals;

/// <summary>
/// Writes a contract as one HTML page: a template from viewer/ with the viewer's script and styles and the
/// contract inlined, so the page opens from a file with no network and can be sent as an attachment.
/// Shared by every tool's docs command.
/// </summary>
internal static partial class ViewerPage
{
    /// <summary>Writes the page to <paramref name="output"/>, titled <paramref name="title"/>.</summary>
    public static void Write(string template, string script, string styles, string spec, string output, string title)
    {
        var viewer = Path.Combine(AppContext.BaseDirectory, "viewer");
        var parts = new Dictionary<string, string>
        {
            ["TITLE"] = WebUtility.HtmlEncode(title),
            // A JSON string with <, > and & escaped, so the contract can't close the script around it.
            ["SPEC"] = $"\"{JsonEncodedText.Encode(File.ReadAllText(spec))}\"",
            ["STYLES"] = File.ReadAllText(Path.Combine(viewer, styles)),
            ["SCRIPT"] = File.ReadAllText(Path.Combine(viewer, script))
                .Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase),
        };

        // One pass, so nothing inlined is searched for placeholders again.
        var source = File.ReadAllText(Path.Combine(viewer, template));
        var page = Placeholder().Replace(source, match => parts[match.Groups[1].Value]);
        File.WriteAllText(output, page);
    }

    [GeneratedRegex("%(TITLE|SPEC|STYLES|SCRIPT)%")]
    private static partial Regex Placeholder();
}
