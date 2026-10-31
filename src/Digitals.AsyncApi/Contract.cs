using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Digitals.AsyncApi;

/// <summary>An operation as the cases need it.</summary>
/// <param name="Id">The operation's key under <c>operations</c>.</param>
/// <param name="Action"><c>send</c> or <c>receive</c>.</param>
/// <param name="Address">The channel's address.</param>
/// <param name="Messages">The messages it sends or receives.</param>
/// <param name="Reply">What the service sends back, for a receive operation with a reply.</param>
internal sealed record EventOperation(
    string Id,
    string Action,
    string Address,
    EventMessage[] Messages,
    EventReply? Reply);

/// <summary>A message, with its traits applied.</summary>
/// <param name="Name">Its <c>name</c>, or its key.</param>
/// <param name="Example">The name of its first example, if any.</param>
/// <param name="Headers">The headers schema.</param>
/// <param name="Payload">The payload schema, when it is JSON Schema.</param>
/// <param name="PayloadName">The name of the schema the payload refers to, if it refers to one.</param>
/// <param name="Keyed">Whether its Kafka binding sets a key.</param>
internal sealed record EventMessage(
    string Name,
    string? Example,
    IOpenApiSchema Headers,
    IOpenApiSchema Payload,
    string? PayloadName,
    bool Keyed);

/// <summary>The reply to a request.</summary>
/// <param name="Destination">Where it goes, as a phrase: "to replies.v1", "to the address in the request's replyTo header".</param>
/// <param name="Messages">The names of the messages the reply can be.</param>
/// <param name="Correlation">Where the reply carries the request's ID, as a phrase, if the contract says.</param>
internal sealed record EventReply(string Destination, string[] Messages, string? Correlation);

/// <summary>
/// Reads an AsyncAPI 3 document for <c>cases</c>. It follows <c>$ref</c>s and applies message traits itself,
/// then reads each payload and headers schema with Microsoft.OpenApi, since an AsyncAPI schema is JSON Schema.
/// </summary>
internal sealed partial class Contract(JsonObject root)
{
    // Each message's headers and payload, as CreateProject.payload, beside the contract's own components.schemas so
    // a $ref resolves and a reader error names the message.
    private readonly JsonObject _schemas = Clone(Object(root, root["components"], "schemas"));

    private readonly Dictionary<JsonObject, MessageDraft> _messages = new(ReferenceEqualityComparer.Instance);

    /// <summary>The operations, or the reasons the contract can't be read, and any warnings.</summary>
    public static (List<EventOperation>? Operations, string[] Problems) Read(string path)
    {
        var (root, unreadable) = ContractYaml.Read(path);
        if (root is null)
        {
            return (null, unreadable);
        }

        var version = $"{root["asyncapi"]}";
        return version.StartsWith("3.", StringComparison.Ordinal)
            ? new Contract(root).Operations(path)
            : (null, [$"cases reads AsyncAPI 3, and this document is '{version}'"]);
    }

    private (List<EventOperation>? Operations, string[] Problems) Operations(string path)
    {
        var drafts = Object(root, root["operations"]).Select(o => Draft(o.Key, Object(root, o.Value))).ToList();
        foreach (var schema in _schemas.Select(p => p.Value))
        {
            Clean(schema);
        }

        var contract = new JsonObject
        {
            ["openapi"] = "3.1.1",
            ["info"] = new JsonObject { ["title"] = "cases", ["version"] = "1" },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject { ["schemas"] = _schemas },
        };
        ReadResult result;
        try
        {
            result = OpenApiYamlReader.Read(contract, new Uri(Path.GetFullPath(path)), new OpenApiReaderSettings());
        }
        catch (FormatException e)
        {
            // The reader parses a number keyword such as minLength without checking it.
            return (null, [$"a schema keyword that takes a number has something else: {e.Message}"]);
        }

        var read = new[] { result.Document }.OfType<OpenApiDocument>().Select(d => d.Components)
            .OfType<OpenApiComponents>().SelectMany(c => c.Schemas.OrEmpty())
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string[] problems =
        [
            .. new[] { result.Diagnostic }.OfType<OpenApiDiagnostic>().SelectMany(d => d.Errors)
                .Select(e => $"a schema can't be read: {e}"),
        ];
        return ([.. drafts.Select(d => d.Build(read))], problems);
    }

    private OperationDraft Draft(string id, JsonObject operation)
    {
        var channel = Object(root, operation["channel"]);
        var messages = Listed(operation, channel).Select(Drafted).ToArray();
        return new(id, $"{operation["action"]}", ChannelAddress(channel), messages, Reply(operation));
    }

    // The operation's own list, or every message on its channel.
    private IEnumerable<JsonObject> Listed(JsonObject operation, JsonObject channel)
    {
        var listed = operation["messages"] is JsonArray refs
            ? refs
            : Object(root, channel["messages"]).Select(m => m.Value);
        return listed.Select(m => Resolve(root, m)).OfType<JsonObject>();
    }

    // A message two operations list is read once.
    private MessageDraft Drafted(JsonObject message)
    {
        if (!_messages.TryGetValue(message, out var draft))
        {
            draft = Message(message);
            _messages[message] = draft;
        }

        return draft;
    }

    private MessageDraft Message(JsonObject message)
    {
        var merged = Traits(message);
        var example = Items(merged["examples"]).OfType<JsonObject>().Select(e => e["name"]).OfType<JsonValue>()
            .Select(v => $"{v}").FirstOrDefault();
        var payload = JsonSchema(merged["payload"]);
        var keyed = At(root, merged, "bindings", "kafka", "key") is not null;
        var name = MessageName(merged, message);
        var headers = Register($"{name}.headers", merged["headers"]);
        return new(name, example, headers, Register($"{name}.payload", payload), Named(payload), keyed);
    }

    // Its name, or its key under messages. A message written inline in an operation's list has neither.
    private static string MessageName(JsonObject merged, JsonObject message)
    {
        if (merged["name"] is JsonValue name)
        {
            return $"{name}";
        }

        return message.Parent is JsonObject ? message.GetPropertyName() : "message";
    }

    private static string? Named(JsonNode? schema) =>
        schema is JsonObject o && o["$ref"] is JsonValue reference ? $"{reference}".Split('/')[^1] : null;

    private EventReply? Reply(JsonObject operation)
    {
        if (Resolve(root, operation["reply"]) is not JsonObject reply)
        {
            return null;
        }

        var channel = Object(root, reply["channel"]);
        var destination = At(root, reply, "address", "location") is JsonValue location
            ? $"to the address in the request's {Location($"{location}")}"
            : $"to {ChannelAddress(channel)}";
        var messages = Listed(reply, channel).Select(m => (Merged: Traits(m), Message: m)).ToArray();
        string[] names = [.. messages.Select(m => MessageName(m.Merged, m.Message))];
        var correlation = messages.Select(m => At(root, m.Merged, "correlationId", "location")).OfType<JsonValue>()
            .Select(l => Location($"{l}")).FirstOrDefault();
        return new(destination, names.Length > 0 ? names : ["reply"], correlation);
    }

    // A null address, which the reader keeps as a sentinel string, means the address is only known at runtime.
    private static string ChannelAddress(JsonObject channel) =>
        channel["address"] is JsonValue address && !JsonNullSentinel.IsJsonNullSentinel(address)
            ? $"{address}"
            : "an address set at runtime";

    // The message with its traits merged in, in order. The message's own fields win, as AsyncAPI 3 says.
    private JsonObject Traits(JsonObject message) =>
        Merge(Items(message["traits"]).Select(t => Object(root, t)).Aggregate(new JsonObject(), Merge), message);

    // JSON Merge Patch, following a $ref on either side where both are objects.
    private JsonObject Merge(JsonObject target, JsonObject patch)
    {
        var merged = Clone(target);
        var copy = Clone(patch);
        foreach (var key in copy.Select(p => p.Key).ToList())
        {
            copy.Remove(key, out var value);
            merged[key] = value is JsonObject over && merged[key] is JsonObject under
                ? Merge(Object(root, under), Object(root, over))
                : value;
        }

        return merged;
    }

    // A payload in another schema format, such as Avro, has no JSON Schema rules to walk. Avro's own format
    // ends in +json, so the check is on the start.
    private JsonNode? JsonSchema(JsonNode? payload)
    {
        if (Resolve(root, payload) is not JsonObject multi || multi["schemaFormat"] is not JsonValue format)
        {
            return payload;
        }

        return JsonSchemaFormats.Any(f => $"{format}".StartsWith(f, StringComparison.OrdinalIgnoreCase))
            ? multi["schema"]
            : null;
    }

    private static readonly string[] JsonSchemaFormats = ["application/vnd.aai.asyncapi", "application/schema+"];

    private string Register(string name, JsonNode? schema)
    {
        // Two messages can share a name.
        var key = name;
        var n = 1;
        while (_schemas.ContainsKey(key))
        {
            key = $"{name}{++n}";
        }

        _schemas[key] = schema?.DeepClone() ?? new JsonObject();
        return key;
    }

    // $message.header#/replyTo reads as "replyTo header", and $message.payload#/request/id as "request.id payload
    // field".
    private static string Location(string location)
    {
        var match = RuntimeExpression().Match(location);
        if (!match.Success)
        {
            return location;
        }

        var field = match.Groups[2].Value.Replace('/', '.');
        return match.Groups[1].Value == "header" ? $"{field} header" : $"{field} payload field";
    }

    // AsyncAPI's discriminator is a property name; OpenAPI's is an object, which the reader would reject.
    private static void Clean(JsonNode? node)
    {
        if (node is JsonObject schema && schema["discriminator"] is JsonValue)
        {
            schema.Remove("discriminator");
        }

        var children = node switch
        {
            JsonObject o => o.Select(p => p.Value),
            JsonArray a => a,
            _ => [],
        };
        foreach (var child in children)
        {
            Clean(child);
        }
    }

    /// <summary>The node a local <c>$ref</c> points to, following a chain of them; a loop gives up after 16.</summary>
    private static JsonNode? Resolve(JsonObject root, JsonNode? node)
    {
        for (var hops = 0; hops < 16 && node is JsonObject o && o["$ref"] is JsonValue reference; hops++)
        {
            node = Pointer(root, $"{reference}");
        }

        return node;
    }

    // Only a reference inside the document: an external one gives nothing, and its schema no rules.
    private static JsonNode? Pointer(JsonObject root, string reference) =>
        reference.StartsWith("#/", StringComparison.Ordinal)
            ? reference[2..].Split('/').Select(key => key.Replace("~1", "/").Replace("~0", "~"))
                .Aggregate((JsonNode?)root, (node, key) => (node as JsonObject)?[key])
            : null;

    // The node at path below node, following a $ref at each step.
    private static JsonNode? At(JsonObject root, JsonNode? node, params string[] path) =>
        path.Aggregate(node, (current, key) => (Resolve(root, current) as JsonObject)?[key]);

    private static JsonObject Object(JsonObject root, JsonNode? node, params string[] path) =>
        Resolve(root, At(root, node, path)) as JsonObject ?? [];

    private static JsonArray Items(JsonNode? node) => node as JsonArray ?? [];

    private static JsonObject Clone(JsonObject node) => node.DeepClone().AsObject();

    [GeneratedRegex(@"^\$message\.(header|payload)#/(.+)$")]
    private static partial Regex RuntimeExpression();

    private sealed record MessageDraft(
        string Name,
        string? Example,
        string Headers,
        string Payload,
        string? PayloadName,
        bool Keyed);

    private sealed record OperationDraft(
        string Id,
        string Action,
        string Address,
        MessageDraft[] Messages,
        EventReply? Reply)
    {
        // A schema the reader couldn't make anything of has no rules.
        public EventOperation Build(Dictionary<string, IOpenApiSchema> read) => new(
            Id,
            Action,
            Address,
            [.. Messages.Select(m => new EventMessage(
                m.Name, m.Example, Schema(read, m.Headers), Schema(read, m.Payload), m.PayloadName, m.Keyed))],
            Reply);

        private static IOpenApiSchema Schema(Dictionary<string, IOpenApiSchema> read, string key) =>
            read.GetValueOrDefault(key, Empty);

        private static readonly OpenApiSchema Empty = new();
    }
}
