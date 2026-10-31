using System.Text.Json;

namespace Digitals.AsyncApi.Tests;

/// <summary>`dt-asyncapi cases`: the test cases a contract implies, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CasesTests
{
    // Every kind of message the cases name and every rule they cover, on one contract. NewProject is built with
    // allOf, nick and alias with YAML merge keys, and Step refers to itself. The imports channel holds the
    // messages the cases can't read: one in Avro, one in another file, one whose $ref leads nowhere, one through
    // a list, and one that refers to itself.
    private const string Contract = """
        asyncapi: 3.0.0
        info: { title: Projects events, version: 1.0.0 }
        x-shared:
          short: &short { type: string, maxLength: 9, x-note: ~ }
          named: &named { minLength: 2 }
        channels:
          created:
            address: projects.v1.created
            messages:
              projectCreated: { $ref: '#/components/messages/ProjectCreated' }
              projectArchived: { $ref: '#/components/messages/ProjectArchived' }
          commands:
            address: projects.v1.commands
            messages:
              createProject: { $ref: '#/components/messages/CreateProject' }
              renameProject: { $ref: '#/components/messages/RenameProject' }
          answers:
            address: projects.v1.answers
            messages:
              answer: { $ref: '#/components/messages/Answer' }
          callbacks:
            address: null
          questions:
            address: projects.v1.questions
            messages:
              question: { payload: { type: string } }
          imports:
            address: projects.v1.imports
            messages:
              avroImport: { $ref: '#/components/messages/AvroImport' }
              jsonImport: { $ref: '#/components/messages/JsonImport' }
              externalImport: { payload: { $ref: 'other.yaml#/Import' } }
              brokenImport: { $ref: '#/components/messages/Missing/payload' }
              listImport: { $ref: '#/components/messages/ProjectCreated/traits/0/headers' }
              loopImport: { $ref: '#/channels/imports/messages/loopImport' }
        operations:
          PublishProject:
            action: send
            channel: { $ref: '#/channels/created' }
          HandleCommand:
            action: receive
            channel: { $ref: '#/channels/commands' }
            messages:
              - $ref: '#/channels/commands/messages/createProject'
              - $ref: '#/channels/commands/messages/createProject'
              - { payload: { type: integer } }
              - { payload: { type: boolean } }
            reply:
              address: { location: $message.header#/replyTo }
              channel: { $ref: '#/channels/callbacks' }
              messages: [{ $ref: '#/channels/answers/messages/answer' }]
          RenameProject:
            action: receive
            channel: { $ref: '#/channels/commands' }
            messages: [{ $ref: '#/channels/commands/messages/renameProject' }]
            reply: { $ref: '#/components/replies/static' }
          AskQuestion:
            action: receive
            channel: { $ref: '#/channels/questions' }
            reply:
              channel: { $ref: '#/channels/callbacks' }
          ImportProjects:
            action: receive
            channel: { $ref: '#/channels/imports' }
        components:
          replies:
            static:
              channel: { $ref: '#/channels/answers' }
              messages: [{ name: Ack, correlationId: { location: requestId } }]
          messageTraits:
            standard:
              headers: { $ref: '#/components/schemas/Headers' }
          messages:
            ProjectCreated:
              name: ProjectCreated
              traits: [{ $ref: '#/components/messageTraits/standard' }]
              bindings: { kafka: { key: { type: string } } }
              payload: { $ref: '#/components/schemas/Project' }
            ProjectArchived:
              name: ProjectArchived
              payload: { type: object, properties: { id: { type: string } } }
            CreateProject:
              name: CreateProject
              traits: [{ $ref: '#/components/messageTraits/standard' }]
              headers: { type: object, required: [replyTo], properties: { replyTo: { type: string, maxLength: 249 } } }
              payload: { $ref: '#/components/schemas/NewProject' }
              examples: [{ summary: No name. }, { name: small, payload: { name: Roof, code: R01 } }]
            RenameProject:
              name: RenameProject
              payload:
                oneOf:
                  - { type: object, properties: { name: { type: string, maxLength: 80 } } }
                  - { type: object, properties: { title: { type: string, maxLength: 80 } } }
            Answer:
              name: Answer
              correlationId: { location: $message.payload#/request/id }
              payload: { type: object }
            AvroImport:
              name: AvroImport
              payload:
                schemaFormat: application/vnd.apache.avro+json;version=1.9.0
                schema: { type: record, name: Import, fields: [{ name: id, type: string }] }
            JsonImport:
              name: JsonImport
              payload:
                schemaFormat: application/schema+json;version=draft-07
                schema: { type: object, required: [source], properties: { source: { type: string, maxLength: 20 } } }
          schemas:
            Headers: { type: object, required: [messageId], properties: { messageId: { type: string, format: uuid } } }
            Project: { type: object, discriminator: kind, properties: { kind: { type: string, maxLength: 10 } } }
            Base:
              type: object
              required: [name]
              properties:
                name: { type: string, minLength: 1, maxLength: 80, pattern: '^[A-Za-z ]+$', example: Roof }
            NewProject:
              allOf:
                - $ref: '#/components/schemas/Base'
                - type: object
                  additionalProperties: false
                  minProperties: 1
                  maxProperties: 40
                  required: [code]
                  properties:
                    id: { type: string, readOnly: true, maxLength: 36 }
                    audit: { type: object, readOnly: true, properties: { by: { type: string, maxLength: 5 } } }
                    code: { type: string, minLength: 3, pattern: '.*' }
                    nick: { <<: [*short, *named] }
                    alias: { <<: *short, maxLength: 4 }
                    note: { type: string, minLength: 0 }
                    digits: { type: string, pattern: '^[0-9]+$', example: 42 }
                    slug: { type: string, pattern: '^[a-z]+$', examples: [ABC] }
                    session: { type: string, pattern: '(' }
                    logo: { type: string, format: binary, minLength: 1, maxLength: 1024 }
                    owner: { type: string, format: email }
                    at: { type: string, format: date-time }
                    secret: { type: string, format: password }
                    budget: { type: number, minimum: 0.5, maximum: 1000, multipleOf: 0.25 }
                    share: { type: number, exclusiveMinimum: 0, exclusiveMaximum: 1 }
                    weeks: { type: integer, format: int32, maximum: 52, multipleOf: 1 }
                    hours: { type: integer, minimum: 1, multipleOf: 5 }
                    count: { type: integer, format: int32 }
                    total: { type: integer, format: int64 }
                    small: { type: integer, format: int64, maximum: 10 }
                    priority: { type: integer, enum: [1, 2, 3] }
                    colour: { type: string, maxLength: 6, enum: [red, orange, yellow, green, blue, indigo] }
                    state: { type: [string, 'null'], enum: [open, null] }
                    kind: { const: project }
                    urgent: { type: boolean }
                    tags: { type: array, minItems: 1, maxItems: 5, uniqueItems: true, items: { type: string, maxLength: 20 } }
                    labels: { type: array, minItems: 0, uniqueItems: false, items: { type: string } }
                    members: { type: array, minItems: 3, items: { $ref: '#/components/schemas/Member' } }
                    settings: { type: object, minProperties: 3 }
                    meta: { type: object, minProperties: 0, properties: { source: { type: string, maxLength: 9 } } }
                    plan: { $ref: '#/components/schemas/Step' }
                    contact:
                      oneOf:
                        - { type: object, properties: { email: { type: string, maxLength: 254 } } }
                        - { type: object, properties: { phone: { type: string, maxLength: 20 } } }
                    extra: { anyOf: [{ type: string, maxLength: 10 }, { type: integer }] }
            Member: { type: object, properties: { name: { type: string, maxLength: 40 } } }
            Step: { type: object, properties: { title: { type: string, maxLength: 30 }, next: { $ref: '#/components/schemas/Step' } } }
        """;

    private static readonly Lazy<(Dictionary<string, JsonElement> Cases, string Output)> Listed = new(() =>
    {
        var (_, output) = Spectral.Tool("cases", Spectral.Write(Contract), "--format", "json");
        using var json = JsonDocument.Parse(output[output.IndexOf('[', StringComparison.Ordinal)..(output.LastIndexOf(']') + 1)]);
        var cases = json.RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.Clone(), StringComparer.Ordinal);
        return (cases, output);
    });

    [Theory]
    [InlineData("PublishProject.ProjectCreated", "Make the service send a ProjectCreated. Expect it on projects.v1.created with the messageId header, a Kafka key and a Project payload.")]
    [InlineData("PublishProject.ProjectArchived", "Make the service send a ProjectArchived. Expect it on projects.v1.created with a matching payload.")]
    [InlineData("HandleCommand.CreateProject", "Send the small example to projects.v1.commands. Expect an Answer to the address in the request's replyTo header, with a request.id payload field that pairs it with the request.")]
    [InlineData("HandleCommand.message", "Send a valid message to projects.v1.commands. Expect an Answer to the address in the request's replyTo header, with a request.id payload field that pairs it with the request.")]
    [InlineData("HandleCommand.duplicate", "Send the same message twice. Expect it processed once.")]
    [InlineData("RenameProject.RenameProject", "Send a valid RenameProject to projects.v1.commands. Expect an Ack to projects.v1.answers, with a requestId that pairs it with the request.")]
    [InlineData("AskQuestion.question", "Send a valid question to projects.v1.questions. Expect a reply to an address set at runtime.")]
    [InlineData("ImportProjects.AvroImport", "Send a valid AvroImport to projects.v1.imports. Expect it processed.")]
    [InlineData("ImportProjects.externalImport", "Send a valid externalImport to projects.v1.imports. Expect it processed.")]
    [InlineData("ImportProjects.loopImport", "Send a valid loopImport to projects.v1.imports. Expect it processed.")]
    public void Cases_Message_SaysWhatToSendAndExpect(string id, string detail) =>
        Assert.Equal(detail, Listed.Value.Cases[id].GetProperty("detail").GetString());

    [Theory]
    [InlineData("HandleCommand.CreateProject.headers.messageId.format", "Rejects \"not-a-uuid\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.headers.replyTo.required", "Rejects a message without replyTo. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.headers.replyTo.maxLength", "Accepts 249 characters, rejects 250. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.type", "Rejects a list. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.minProperties", "Accepts 1 field, rejects an empty object. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.maxProperties", "Accepts 40 fields, rejects 41. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.additionalProperties", "Rejects a field the schema doesn't define. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.name.required", "Rejects a message without name. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.name.type", "Rejects 42. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.name.minLength", "Accepts 1 character, rejects an empty value. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.name.pattern", "Accepts \"Roof\", rejects \"!\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.audit.by.maxLength", "Accepts 5 characters, rejects 6. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.code.pattern", "Accepts a value matching .*, rejects a value that breaks it. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.nick.minLength", "Accepts 2 characters, rejects 1. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.nick.maxLength", "Accepts 9 characters, rejects 10. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.alias.maxLength", "Accepts 4 characters, rejects 5. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.digits.pattern", "Accepts a value matching ^[0-9]+$, rejects \"!\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.slug.pattern", "Accepts a value matching ^[a-z]+$, rejects \"!\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.session.pattern", "Accepts a value matching (, rejects a value that breaks it. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.logo.maxLength", "Accepts 1024 bytes, rejects 1025. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.owner.format", "Rejects \"no-at-sign\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.budget.type", "Rejects \"abc\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.budget.minimum", "Accepts 0.5, rejects 0.25. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.budget.multipleOf", "Accepts 0.25, rejects 0.375. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.share.exclusiveMinimum", "Accepts 0.01, rejects 0. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.share.exclusiveMaximum", "Accepts 0.99, rejects 1. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.hours.minimum", "Accepts 1, rejects -4. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.hours.multipleOf", "Accepts 5, rejects 6. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.count.format", "Rejects 2147483648 (past int32). Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.total.format", "Rejects 9223372036854775808 (past int64). Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.small.maximum", "Accepts 10, rejects 11. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.priority.enum", "Accepts 1, 2 and 3, rejects 4. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.colour.enum", "Accepts each of its 6 values, rejects \"not-red\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.state.enum", "Accepts \"open\" and null, rejects \"not-open\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.kind.const", "Accepts \"project\", rejects \"not-project\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.urgent.type", "Rejects \"yes\". Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.tags.type", "Rejects an object. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.tags.minItems", "Accepts 1 item, rejects an empty list. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.tags.uniqueItems", "Rejects two equal items. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.tags[].maxLength", "Accepts 20 characters, rejects 21. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.members.minItems", "Accepts 3 items, rejects 2. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.members[].name.maxLength", "Accepts 40 characters, rejects 41. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.settings.minProperties", "Accepts 3 fields, rejects 2. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.plan.title.maxLength", "Accepts 30 characters, rejects 31. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.contact.oneOf", "Rejects a value for contact that matches none of its 2 variants. Expect it not processed.")]
    [InlineData("HandleCommand.CreateProject.payload.extra.anyOf", "Rejects a value for extra that matches none of its 2 variants. Expect it not processed.")]
    [InlineData("HandleCommand.message.payload.type", "Rejects \"abc\". Expect it not processed.")]
    [InlineData("RenameProject.RenameProject.payload.oneOf", "Rejects a payload that matches none of its 2 variants. Expect it not processed.")]
    [InlineData("ImportProjects.JsonImport.payload.source.required", "Rejects a message without source. Expect it not processed.")]
    public void Cases_ReceivedMessageRule_SaysWhatIsAcceptedAndRejected(string id, string detail) =>
        Assert.Equal(detail, Listed.Value.Cases[id].GetProperty("detail").GetString());

    // Headers are always a map, the message's own required list replaces its trait's, a sent message is the
    // service's to get right, an Avro payload has no JSON Schema, a password has no format to break, an int32 or
    // int64 with a maximum is bounded by it, every integer is a multiple of 1, and a $ref that leads nowhere is
    // no message.
    [Theory]
    [InlineData("HandleCommand.CreateProject.headers.type")]
    [InlineData("HandleCommand.CreateProject.headers.messageId.required")]
    [InlineData("PublishProject.ProjectCreated.payload.type")]
    [InlineData("PublishProject.ProjectCreated.headers.messageId.format")]
    [InlineData("ImportProjects.AvroImport.payload.type")]
    [InlineData("HandleCommand.CreateProject.payload.secret.format")]
    [InlineData("HandleCommand.CreateProject.payload.weeks.format")]
    [InlineData("HandleCommand.CreateProject.payload.weeks.multipleOf")]
    [InlineData("HandleCommand.CreateProject.payload.small.format")]
    [InlineData("HandleCommand.CreateProject.payload.colour.maxLength")]
    [InlineData("HandleCommand.CreateProject.payload.note.minLength")]
    [InlineData("HandleCommand.CreateProject.payload.labels.minItems")]
    [InlineData("HandleCommand.CreateProject.payload.labels.uniqueItems")]
    [InlineData("HandleCommand.CreateProject.payload.meta.minProperties")]
    [InlineData("HandleCommand.CreateProject.payload.plan.next.next.title.maxLength")]
    [InlineData("ImportProjects.brokenImport")]
    [InlineData("ImportProjects.listImport")]
    public void Cases_RuleThatCantFail_IsNotListed(string id) =>
        Assert.DoesNotContain(id, Listed.Value.Cases.Keys);

    // AsyncAPI's discriminator names a property, where OpenAPI's is an object; the cases read it all the same.
    [Fact]
    public void Cases_Contract_ReadsEverySchema() =>
        Assert.DoesNotContain("can't be read", Listed.Value.Output, StringComparison.Ordinal);

    [Fact]
    public void Cases_Json_GivesEachCaseItsOperationActionAndChannel()
    {
        var found = Listed.Value.Cases["HandleCommand.CreateProject.payload.name.type"];

        Assert.Equal(
            ("HandleCommand", "receive", "projects.v1.commands"),
            (found.GetProperty("operation").GetString(), found.GetProperty("action").GetString(),
                found.GetProperty("channel").GetString()));
    }

    [Fact]
    public void Cases_Text_ListsEachOperationUnderItsActionAndChannel()
    {
        var (exitCode, output) = Spectral.Tool("cases", Spectral.Write(Contract));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "AskQuestion  receive projects.v1.questions\n  AskQuestion.question               Send a valid question",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Contains($"{Listed.Value.Cases.Count} cases in 5 operations", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("messages", "HandleCommand.duplicate", "HandleCommand.message.payload.type")]
    [InlineData("rules", "HandleCommand.message.payload.type", "HandleCommand.duplicate")]
    public void Cases_Only_ListsOneKind(string only, string listed, string left)
    {
        var output = Spectral.Tool("cases", Spectral.Write(Contract), "--only", only).Output;

        Assert.Contains($"  {listed} ", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"  {left} ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cases_ExampleContract_ExpectsTheReplyWhereTheRequestSays()
    {
        var (exitCode, output) = Spectral.Tool("cases", Path.Combine(AppContext.BaseDirectory, "examples", "asyncapi.yaml"));

        Assert.Equal(0, exitCode);
        Assert.Contains("Expect an AvailabilityReplied to the address in the request's replyTo header, with a correlationId header that pairs it with the request.", output, StringComparison.Ordinal);
        Assert.Contains("PublishBookingConfirmed.BookingConfirmed  Make the service send a BookingConfirmed.", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--colour", "red", "unknown option '--colour'")]
    [InlineData("--format", "xml", "--format takes text or json")]
    [InlineData("--only", null, "--only takes messages or rules")]
    public void Cases_BadOption_ExitsTwo(string option, string? value, string message)
    {
        string[] args = ["cases", Spectral.Write(Contract), option, .. value is null ? [] : new[] { value }];

        var (exitCode, output) = Spectral.Tool(args);

        Assert.Equal((2, true), (exitCode, output.Contains(message, StringComparison.Ordinal)));
    }

    [Fact]
    public void Cases_NoFile_ExitsTwo()
    {
        var (exitCode, output) = Spectral.Tool("cases", "missing.yaml");

        Assert.Equal((2, true), (exitCode, output.Contains("no file at missing.yaml", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("channels: [1, 2\n", "did not find expected ',' or ']'")]
    [InlineData("", "the file holds no YAML or JSON object")]
    [InlineData("just text\n", "the file holds no YAML or JSON object")]
    [InlineData("asyncapi: 2.6.0\n", "cases reads AsyncAPI 3, and this document is '2.6.0'")]
    [InlineData("info: { title: T, version: 1.0.0 }\n", "cases reads AsyncAPI 3, and this document is ''")]
    [InlineData(Broken + "{ minLength: abc }\n", "a schema keyword that takes a number has something else")]
    public void Cases_UnreadableContract_ExitsOneAndSaysWhy(string yaml, string message)
    {
        var (exitCode, output) = Spectral.Tool("cases", Spectral.Write(yaml));

        Assert.Equal((1, true), (exitCode, output.Contains(message, StringComparison.Ordinal)));
    }

    // A schema the reader can't read is named by its message, and the rest of the contract still gives cases.
    [Fact]
    public void Cases_UnreadableSchema_WarnsAndListsTheRest()
    {
        var (exitCode, output) = Spectral.Tool("cases", Spectral.Write(Broken + "{ items: [1] }\n"));

        Assert.Equal(0, exitCode);
        Assert.Contains("a schema can't be read: schema must be a map/object [#/components/schemas/m.payload/items]", output, StringComparison.Ordinal);
        Assert.Contains("2 cases in 1 operation\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    // A contract with one message, whose payload follows.
    private const string Broken = """
        asyncapi: 3.0.0
        info: { title: T, version: 1.0.0 }
        channels: { c: { address: a } }
        operations: { Op: { action: receive, channel: { $ref: '#/channels/c' }, messages: [{ $ref: '#/components/messages/m' }] } }
        components:
          messages:
            m:
              payload:
        """ + " ";
}
