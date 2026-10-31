namespace Digitals;

/// <summary>Counts and lists worded for a case's detail. Shared by every tool's <c>cases</c> command.</summary>
internal static class Sentences
{
    /// <summary>"1 case", "2 cases".</summary>
    public static string Count(int count, string noun) => $"{count} {Plural(count, noun)}";

    /// <summary>The noun, with an s unless there is one.</summary>
    public static string Plural(int count, string noun) => count == 1 ? noun : $"{noun}s";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string And(string[] items) => Join(items, "and");

    /// <summary>The items in a sentence, the last two joined by <paramref name="last"/>.</summary>
    public static string Join(string[] items, string last) =>
        items.Length == 1 ? items[0] : $"{string.Join(", ", items[..^1])} {last} {items[^1]}";
}
