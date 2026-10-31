using System.Text.Json.Serialization;
using static Digitals.SchemaRules;
using static Digitals.Sentences;

namespace Digitals.AsyncApi;

/// <summary>One behaviour a test should show, with the ID a test can be named by.</summary>
/// <param name="Id">The operation's key, then the message and the field and its rule.</param>
/// <param name="Operation">The operation's key.</param>
/// <param name="Action"><c>send</c> or <c>receive</c>.</param>
/// <param name="Channel">The channel's address.</param>
/// <param name="Detail">What to do and what to expect.</param>
internal sealed record TestCase(string Id, string Operation, string Action, string Channel, string Detail)
    : ITestCase;

/// <summary>The JSON for a list of <see cref="TestCase"/>, generated at compile time for native AOT.</summary>
[JsonSerializable(typeof(List<TestCase>))]
internal sealed partial class TestCaseJson : JsonSerializerContext;

/// <summary>
/// The test cases a contract implies: one per message an operation sends or receives, one for a duplicate of a
/// received message, and one per rule on each received message's headers and payload, field by field. An ID
/// starts with the operation's key, so it stays the same as long as the operation and the rule do.
/// </summary>
internal static class TestCases
{
    private static readonly SchemaRules HeaderRules = new("message", "a set of headers", JsonWrongTypes);
    private static readonly SchemaRules PayloadRules = new("message", "a payload", JsonWrongTypes);

    /// <summary>Every case, operation by operation in the contract's order.</summary>
    public static List<TestCase> From(List<EventOperation> operations, bool messages, bool rules)
    {
        var cases = new List<TestCase>();
        foreach (var operation in operations)
        {
            var found = new List<TestCase>();
            var receive = operation.Action == "receive";
            if (messages)
            {
                found.AddRange(operation.Messages.Select(m => Case(operation, m.Name, Message(operation, m))));
                found.AddRange(receive ? [Case(operation, "duplicate", Duplicate)] : []);
            }

            if (rules && receive)
            {
                found.AddRange(operation.Messages.SelectMany(m => Rules(operation, m)));
            }

            cases.AddRange(found.DistinctBy(c => c.Id));
        }

        return cases;
    }

    private const string Duplicate = "Send the same message twice. Expect it processed once.";

    private static TestCase Case(EventOperation operation, string suffix, string detail) =>
        new($"{operation.Id}.{suffix}", operation.Id, operation.Action, operation.Address, detail);

    private static string Message(EventOperation operation, EventMessage message) =>
        operation.Action == "receive" ? Received(operation, message) : Sent(operation, message);

    private static string Sent(EventOperation operation, EventMessage message)
    {
        string[] headers = [.. message.Headers.Properties.OrEmpty().Select(p => p.Key)];
        string[] expected =
        [
            .. headers.Length > 0 ? [$"the {And(headers)} {Plural(headers.Length, "header")}"] : Array.Empty<string>(),
            .. message.Keyed ? ["a Kafka key"] : Array.Empty<string>(),
            $"{Article(message.PayloadName)} payload",
        ];
        return $"Make the service send {Article(message.Name)}. Expect it on {operation.Address} with {And(expected)}.";
    }

    private static string Received(EventOperation operation, EventMessage message)
    {
        var send = message.Example is { } example
            ? $"Send the {example} example to {operation.Address}"
            : $"Send a valid {message.Name} to {operation.Address}";
        if (operation.Reply is not { } reply)
        {
            return $"{send}. Expect it processed.";
        }

        var answer = $"{Article(Join(reply.Messages, "or"))} {reply.Destination}";
        var paired = reply.Correlation is { } correlation
            ? $", with a {correlation} that pairs it with the request"
            : "";
        return $"{send}. Expect {answer}{paired}.";
    }

    // Headers are always a map, so a set of them can't be the wrong type.
    private static IEnumerable<TestCase> Rules(EventOperation operation, EventMessage message) =>
        HeaderRules.Walk("", message.Headers, required: false, example: null)
            .Where(rule => rule is not { Field: "", Name: "type" })
            .Select(rule => Rule(operation, $"{message.Name}.headers", rule))
            .Concat(PayloadRules.Walk("", message.Payload, required: false, example: null)
                .Select(rule => Rule(operation, $"{message.Name}.payload", rule)));

    // A consumer has no one to answer with a status, so a message it can't take is one it doesn't act on.
    private static TestCase Rule(EventOperation operation, string location, SchemaRule rule) =>
        Case(operation, rule.Id(location), $"{rule.Detail}. Expect it not processed.");

    // "a BookingConfirmed", "an AvailabilityReplied", or "a matching" for a payload with no named schema.
    private static string Article(string? name)
    {
        if (name is null)
        {
            return "a matching";
        }

        return "AEIOU".Contains(char.ToUpperInvariant(name[0]), StringComparison.Ordinal) ? $"an {name}" : $"a {name}";
    }
}
