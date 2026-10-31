using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using static Digitals.SchemaRules;
using static Digitals.Sentences;

namespace Digitals.OpenApi;

/// <summary>One behaviour a test should show, with the ID a test can be named by.</summary>
/// <param name="Id">The operationId, then the status or the input and its rule.</param>
/// <param name="Operation">The operationId.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path template.</param>
/// <param name="Status">The status the test expects.</param>
/// <param name="Detail">What to send and what to expect.</param>
internal sealed record TestCase(string Id, string Operation, string Method, string Path, string Status, string Detail)
    : ITestCase;

/// <summary>
/// The test cases a contract implies: one per documented response, and one per rule on each input, its
/// parameters and its request body field by field. An ID starts with the operationId, so it stays the same
/// as long as the operation and the rule do.
/// </summary>
internal static partial class TestCases
{
    /// <summary>Every case, operation by operation in the contract's order.</summary>
    public static List<TestCase> From(OpenApiDocument document, bool responses, bool rules)
    {
        var ids = Ids(document);
        var cases = new List<TestCase>();
        foreach (var (path, item) in document.Paths)
        {
            foreach (var (method, spec) in item.Operations.OrEmpty())
            {
                var operation = new Operation(ids[spec], method.Method, path, spec, document.Security);
                var found = new List<TestCase>();
                if (responses)
                {
                    found.AddRange(spec.Responses.OrEmpty()
                        .Select(r => operation.Case(r.Key, r.Key, Response(operation, r.Key, r.Value))));
                }

                if (rules)
                {
                    found.AddRange(Parameters(item, spec).SelectMany(p => Parameter(operation, p)));
                    found.AddRange(Body(operation));
                }

                cases.AddRange(found.DistinctBy(c => c.Id));
            }
        }

        return cases;
    }

    /// <summary>A warning when an operation has no operationId, naming the ID its cases start with instead.</summary>
    public static string[] Unnamed(OpenApiDocument document)
    {
        var ids = Ids(document);
        string[] made =
        [
            .. Operations(document).Where(o => string.IsNullOrEmpty(o.Spec.OperationId)).Select(o => ids[o.Spec]),
        ];
        return made.Length == 0
            ? []
            : [$"{Count(made.Length, "operation")} without an operationId: each case ID starts with a name made from "
                + $"the method and route instead, such as {made[0]}"];
    }

    // An operation's operationId, or for one without, a name made from its route that no other operation has.
    private static Dictionary<OpenApiOperation, string> Ids(OpenApiDocument document)
    {
        var operations = Operations(document).ToList();
        var taken = operations.Select(o => o.Spec.OperationId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var ids = new Dictionary<OpenApiOperation, string>();
        foreach (var (path, method, spec) in operations)
        {
            ids[spec] = string.IsNullOrEmpty(spec.OperationId) ? Unique(Route(method, path), taken) : spec.OperationId;
        }

        return ids;
    }

    private static IEnumerable<(string Path, string Method, OpenApiOperation Spec)> Operations(
        OpenApiDocument document) =>
        document.Paths.SelectMany(p => p.Value.Operations.OrEmpty().Select(o => (p.Key, o.Key.Method, o.Value)));

    // POST /api/v1.0/orders/{orderId}:cancel is PostOrdersByOrderIdCancel: "api" and the version say nothing, and a
    // parameter reads as "by".
    private static string Route(string method, string path)
    {
        var words = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !RouteNoise().IsMatch(segment))
            .Select(segment => PathParameter().Replace(segment, m => $" by {m.Groups[1].Value} "))
            .SelectMany(segment => Word().Matches(segment).Select(m => m.Value))
            .Prepend(method.ToLowerInvariant());
        return string.Concat(words.Select(w => $"{char.ToUpperInvariant(w[0])}{w[1..]}"));
    }

    private static string Unique(string id, HashSet<string> taken)
    {
        var unique = id;
        var n = 1;
        while (!taken.Add(unique))
        {
            unique = $"{id}{++n}";
        }

        return unique;
    }

    private sealed record Operation(
        string Id,
        string Method,
        string Path,
        OpenApiOperation Spec,
        IList<OpenApiSecurityRequirement>? DocumentSecurity)
    {
        /// <summary>For a request the service can't read: a value of the wrong type, a field it doesn't know.</summary>
        public string Unreadable => Documented("400", "422");

        /// <summary>For a request it reads but that breaks a rule: a length, a range, a format.</summary>
        public string Invalid => Documented("422", "400");

        public TestCase Case(string suffix, string status, string detail) =>
            new($"{Id}.{suffix}", Id, Method, Path, status, detail);

        // A missing body field is read and found invalid; a missing parameter leaves the request unreadable.
        public TestCase Rule(string location, SchemaRule rule, bool inBody)
        {
            var invalid = rule.Kind == RuleKind.Invalid || rule.Kind == RuleKind.Missing && inBody;
            var status = invalid ? Invalid : Unreadable;
            return Case(rule.Id(location), status, $"{rule.Detail} with {status}.");
        }

        // The second only when the operation documents it and not the first.
        private string Documented(string first, string second) =>
            Has(second) && !Has(first) ? second : first;

        private bool Has(string status) => Spec.Responses.OrEmpty().Any(r => r.Key == status);
    }

    private static string Response(Operation operation, string status, IOpenApiResponse response)
    {
        string[] expected = [.. Headers(response), .. Bodies(response)];
        var with = expected.Length > 0 ? $" with {And(expected)}" : "";
        return $"{Action(operation, status)}. Expect {status}{with}.";
    }

    private static IEnumerable<string> Headers(IOpenApiResponse response)
    {
        string[] headers = [.. response.Headers.OrEmpty().Select(h => h.Key)];
        return headers.Length > 0 ? [$"the {And(headers)} {Plural(headers.Length, "header")}"] : [];
    }

    // The JSON body when the response offers one, as for a request.
    private static IEnumerable<string> Bodies(IOpenApiResponse response) =>
        response.Content.OrEmpty().OrderBy(c => IsJson(c.Key) ? 0 : 1).Take(1).Select(ResponseBody);

    private static string ResponseBody(KeyValuePair<string, IOpenApiMediaType> content)
    {
        var name = content.Value.Schema is OpenApiSchemaReference { Reference.Id: { } id } ? id : "matching";
        var type = content.Key == "application/json" ? "" : $" as {content.Key}";
        return $"{("AEIOU".Contains(name[0], StringComparison.Ordinal) ? "an" : "a")} {name} body{type}";
    }

    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        ["304"] = "Send the ETag of an earlier response as If-None-Match",
        ["401"] = "Send no credentials",
        ["406"] = "Ask for a media type the operation doesn't offer in Accept",
        ["409"] = "Send a request that conflicts with the current state",
        ["412"] = "Send an If-Match ETag that no longer matches",
        ["413"] = "Send a body over the size limit",
        ["415"] = "Send the body with a Content-Type the operation doesn't take",
        ["428"] = "Leave out If-Match",
        ["429"] = "Go over the rate limit",
    };

    private static string Action(Operation operation, string status)
    {
        if (Actions.TryGetValue(status, out var action))
        {
            return action;
        }

        var input = HasBody(operation) ? "a body" : "a parameter";
        return status switch
        {
            "400" => $"Send {input} the service can't read",
            "403" => Forbidden(operation),
            "404" => NotFound(operation),
            "422" => $"Send {input} that breaks one of its rules",
            ['2', _, _] => Success(operation),
            ['5', _, _] => "Make a dependency fail",
            _ => "Bring about this response",
        };
    }

    private static string Forbidden(Operation operation)
    {
        string[] scopes =
        [
            .. (operation.Spec.Security ?? operation.DocumentSecurity).OrEmpty()
                .SelectMany(requirement => requirement.Values.SelectMany(scopes => scopes))
                .Distinct(StringComparer.Ordinal),
        ];
        return scopes.Length > 0
            ? $"Send credentials without the {And(scopes)} {Plural(scopes.Length, "scope")}"
            : "Send credentials without access";
    }

    private static string NotFound(Operation operation)
    {
        string[] names = [.. PathParameter().Matches(operation.Path).Select(m => m.Groups[1].Value)];
        return names.Length > 0 ? $"Use an unknown {Or(names)}" : "Ask for something that doesn't exist";
    }

    private static string Success(Operation operation) =>
        RequestBody(operation.Spec)?.Value.Examples.OrEmpty().FirstOrDefault().Key is { } example
            ? $"Send the {example} example"
            : "Send a valid request";

    // An operation's parameters override the path's of the same name and location.
    private static IEnumerable<IOpenApiParameter> Parameters(IOpenApiPathItem item, OpenApiOperation operation)
    {
        var own = operation.Parameters.OrEmpty().ToList();
        return item.Parameters.OrEmpty().Where(p => !own.Any(o => o.Name == p.Name && o.In == p.In)).Concat(own);
    }

    private static readonly SchemaRules ParameterRules = new("request", "a body", TextWrongTypes);

    private static IEnumerable<TestCase> Parameter(Operation operation, IOpenApiParameter parameter)
    {
        var location = $"{parameter.In}";
        var name = $"{char.ToLowerInvariant(location[0])}{location[1..]}";
        var required = parameter.Required && parameter.In != ParameterLocation.Path;
        return ParameterRules.Walk(Name(parameter.Name), parameter.Schema, required, parameter.Example)
            .Select(rule => operation.Rule(name, rule, inBody: false));
    }

    private static IEnumerable<TestCase> Body(Operation operation)
    {
        if (RequestBody(operation.Spec) is not { Key: var media, Value: var body })
        {
            return [];
        }

        // A read-only field is the service's to set, so a request has no rule for it.
        var rules = new SchemaRules("request", "a body", IsJson(media) ? JsonWrongTypes : TextWrongTypes);
        return rules.Walk("", body.Schema, required: false, example: null)
            .Where(rule => !rule.ReadOnly)
            .Select(rule => operation.Rule("body", rule, inBody: true));
    }

    private static bool HasBody(Operation operation) => RequestBody(operation.Spec) is not null;

    // JSON when the operation takes it, since that's what the field rules are written for.
    private static KeyValuePair<string, IOpenApiMediaType>? RequestBody(OpenApiOperation operation) =>
        operation.RequestBody?.Content.OrEmpty()
            .OrderBy(c => IsJson(c.Key) ? 0 : 1)
            .Select(c => (KeyValuePair<string, IOpenApiMediaType>?)c)
            .FirstOrDefault();

    private static bool IsJson(string media) =>
        media == "application/json" || media.EndsWith("+json", StringComparison.Ordinal);

    private static string Or(string[] items) => Join(items, "or");

    // A name the parser requires but types as string?: AsSpan reads null as "" without a branch no contract reaches.
    private static string Name(string? name) => name.AsSpan().ToString();

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex PathParameter();

    [GeneratedRegex(@"^(api|v[0-9]+(\.[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RouteNoise();

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Word();
}
