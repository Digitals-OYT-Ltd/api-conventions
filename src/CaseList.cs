using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Digitals;

/// <summary>A test case the <c>cases</c> commands list.</summary>
internal interface ITestCase
{
    /// <summary>The ID a test can be named by. It starts with the operation's.</summary>
    string Id { get; }

    /// <summary>The operation's ID.</summary>
    string Operation { get; }

    /// <summary>What to do and what to expect.</summary>
    string Detail { get; }
}

/// <summary>
/// Prints test cases as text for a reader or JSON for an agent. Shared by every tool's <c>cases</c> command.
/// </summary>
internal static class CaseList
{
    /// <summary>
    /// Prints <paramref name="cases"/>: as JSON, with the tool's compile-time <paramref name="types"/> so it
    /// runs without reflection, or as text with each operation under its <paramref name="heading"/> and a
    /// count on stderr.
    /// </summary>
    public static void Write<T>(List<T> cases, IJsonTypeInfoResolver types, bool json, Func<T, string> heading)
        where T : ITestCase
    {
        if (json)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = types,
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(cases, options.GetTypeInfo(typeof(List<T>))));
            return;
        }

        foreach (var operation in cases.GroupBy(c => c.Operation))
        {
            var width = operation.Max(c => c.Id.Length);
            Console.Out.WriteLine($"{operation.Key}  {heading(operation.First())}");
            foreach (var found in operation)
            {
                Console.Out.WriteLine($"  {found.Id.PadRight(width)}  {found.Detail}");
            }

            Console.Out.WriteLine();
        }

        var operations = cases.Select(c => c.Operation).Distinct(StringComparer.Ordinal).Count();
        var count = $"{Sentences.Count(cases.Count, "case")} in {Sentences.Count(operations, "operation")}";
        Console.Error.WriteLine(count);
    }
}
