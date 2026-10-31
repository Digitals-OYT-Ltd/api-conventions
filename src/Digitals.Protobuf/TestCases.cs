using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Digitals.Protobuf;

/// <summary>One behaviour a test should show, with the ID a test can be named by.</summary>
/// <param name="Id">The RPC's ID, then the status or the field and its rule.</param>
/// <param name="Operation">The RPC's ID: the service and the method.</param>
/// <param name="Kind">unary, client streaming, server streaming or bidirectional streaming.</param>
/// <param name="Path">The gRPC path.</param>
/// <param name="Deprecated">Whether the method is marked deprecated.</param>
/// <param name="Detail">What to do and what to expect.</param>
internal sealed record TestCase(string Id, string Operation, string Kind, string Path, bool Deprecated, string Detail)
    : ITestCase;

/// <summary>
/// The test cases a contract implies: one for a call that succeeds, one for each status the method's comment
/// names, one for a retried request_id and an unknown field mask path, and one per protovalidate rule on the
/// request, field by field. An ID starts with the RPC's, so it stays the same as long as the RPC and the rule do.
/// </summary>
internal static partial class TestCases
{
    /// <summary>Every case, RPC by RPC in the contract's order.</summary>
    public static List<TestCase> From(Contract contract, bool calls, bool rules)
    {
        var walker = new Protovalidate(contract);
        var cases = new List<TestCase>();
        foreach (var rpc in contract.Rpcs)
        {
            var fields = contract.Messages.TryGetValue(rpc.Request, out var request)
                ? Contract.Objects(request["field"]).ToArray()
                : [];
            var found = new List<TestCase>();
            if (calls)
            {
                found.Add(Case(rpc, "OK", Succeeds(rpc)));
                found.AddRange(Statuses(rpc));
                found.AddRange(Retry(rpc, fields));
                found.AddRange(Masks(rpc, fields));
            }

            if (rules)
            {
                found.AddRange(walker.Walk(rpc.Request)
                    .Select(rule => Case(rpc, rule.Id, $"{rule.Detail} with INVALID_ARGUMENT.")));
            }

            cases.AddRange(found.DistinctBy(c => c.Id));
        }

        return cases;
    }

    private static TestCase Case(Rpc rpc, string suffix, string detail) =>
        new($"{rpc.Id}.{suffix}", rpc.Id, rpc.Kind, rpc.Path, rpc.Deprecated, detail);

    private static string Succeeds(Rpc rpc)
    {
        var request = Name(rpc.Request);
        var response = Name(rpc.Response);
        var send = rpc.Kind is "unary" or "server streaming"
            ? $"Send a valid {request}"
            : $"Send a stream of valid {request} messages and close it";
        var expect = rpc.Kind switch
        {
            "unary" or "client streaming" => rpc.Response == ".google.protobuf.Empty"
                ? "OK"
                : $"OK with {Article(response)}",
            _ => $"a stream of {response} messages, then OK",
        };
        return $"{send}. Expect {expect}.";
    }

    // A status the method's own comment names, with the condition after it when there is one: "NOT_FOUND when
    // no booking has the ID".
    private static IEnumerable<TestCase> Statuses(Rpc rpc) =>
        StatusPattern().Matches(rpc.Comment)
            .DistinctBy(m => m.Groups["status"].Value)
            .Select(m =>
            {
                var status = m.Groups["status"].Value;
                var action = m.Groups["when"].Success
                    ? $"Call it when {m.Groups["when"].Value.Trim()}"
                    : Actions.GetValueOrDefault(status, "Bring about this status");
                return Case(rpc, status, $"{action}. Expect {status}.");
            });

    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        ["CANCELLED"] = "Cancel the call before it completes",
        ["INVALID_ARGUMENT"] = "Send a request that breaks one of its rules",
        ["DEADLINE_EXCEEDED"] = "Call with a deadline shorter than the call takes",
        ["NOT_FOUND"] = "Ask for something that doesn't exist",
        ["ALREADY_EXISTS"] = "Create something that already exists",
        ["PERMISSION_DENIED"] = "Call with credentials that lack the permission",
        ["RESOURCE_EXHAUSTED"] = "Go over a quota or rate limit",
        ["FAILED_PRECONDITION"] = "Call when the state isn't the one the call needs",
        ["ABORTED"] = "Make the call conflict with a concurrent change",
        ["OUT_OF_RANGE"] = "Ask for something past the end",
        ["UNIMPLEMENTED"] = "Call a method the server doesn't implement",
        ["INTERNAL"] = "Make a dependency fail",
        ["UNAVAILABLE"] = "Make the service unavailable",
        ["UNAUTHENTICATED"] = "Call with no credentials",
    };

    [GeneratedRegex(@"\b(?<status>CANCELLED|UNKNOWN|INVALID_ARGUMENT|DEADLINE_EXCEEDED|NOT_FOUND|ALREADY_EXISTS|" +
        @"PERMISSION_DENIED|RESOURCE_EXHAUSTED|FAILED_PRECONDITION|ABORTED|OUT_OF_RANGE|UNIMPLEMENTED|INTERNAL|" +
        @"UNAVAILABLE|DATA_LOSS|UNAUTHENTICATED)\b(?: (?:when|if) (?<when>[^.;,]+))?")]
    private static partial Regex StatusPattern();

    // A request_id makes a retry safe, so a retry must not do the work twice.
    private static IEnumerable<TestCase> Retry(Rpc rpc, JsonObject[] fields) =>
        fields.Any(f => (string?)f["name"] == "request_id")
            ? [Case(rpc, "retry", "Send the same request twice with the same request_id. Expect the first result " +
                "both times and the change made once.")]
            : [];

    private static IEnumerable<TestCase> Masks(Rpc rpc, JsonObject[] fields) =>
        fields.Where(f => (string?)f["typeName"] == ".google.protobuf.FieldMask")
            .Select(f => (string)f["name"]!)
            .Select(mask => Case(rpc, $"{mask}.unknown_path",
                $"Send {Article(mask)} naming a field that doesn't exist. Expect INVALID_ARGUMENT."));

    // .digitals.bookings.v1.GetBookingRequest is GetBookingRequest.
    private static string Name(string type) => type[(type.LastIndexOf('.') + 1)..];

    // "a Booking", "an Attachment".
    private static string Article(string name) =>
        "AEIOU".Contains(char.ToUpperInvariant(name[0]), StringComparison.Ordinal) ? $"an {name}" : $"a {name}";
}
