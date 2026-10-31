using System.Text.Json;

namespace Digitals.OpenApi.Tests;

/// <summary>`dt-openapi cases`: the test cases a contract implies, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CasesTests
{
    // Every response the cases name and every rule they cover, on one contract. NewProject is built with allOf,
    // nick and alias with YAML merge keys, and Step refers to itself.
    private const string Contract = """
        openapi: 3.1.1
        info: { title: Projects, version: v1 }
        security: [{ oauth: [projects.read, projects.admin] }]
        x-shared:
          short: &short { type: string, maxLength: 9, x-note: ~ }
          named: &named { minLength: 2 }
        paths:
          /api/v1/projects:
            post:
              operationId: CreateProject
              security: [{ bearer: [] }]
              requestBody:
                content:
                  text/plain: { schema: { type: string } }
                  application/json:
                    schema: { $ref: '#/components/schemas/NewProject' }
                    examples: { small: { value: { name: Roof, code: R01 } } }
              responses:
                '201': { description: Made., headers: { Location: { schema: { type: string } } }, content: { text/plain: { schema: { type: string } }, application/json: { schema: { $ref: '#/components/schemas/Project' } } } }
                '400': { description: Unreadable. }
                '403': { description: Forbidden. }
                '404': { description: Missing. }
                '409': { description: Conflict. }
                '412': { description: Stale. }
                '413': { description: Too big. }
                '415': { description: Wrong type. }
                '422': { description: Invalid., content: { application/problem+json: { schema: { $ref: '#/components/schemas/Problem' } } } }
                '428': { description: Unconditional. }
                '429': { description: Too many. }
          /api/v1/projects/{projectId}:
            parameters:
              - { name: projectId, in: path, required: true, schema: { type: string, format: uuid } }
              - { name: X-Trace, in: header, schema: { type: string, maxLength: 64 } }
            get:
              operationId: GetProject
              parameters:
                - { name: X-Trace, in: header, required: true, schema: { type: string, minLength: 3 } }
                - { name: If-None-Match, in: header, example: '"7"', schema: { type: string, pattern: '^"[0-9]+"$' } }
                - { name: archived, in: query, schema: { type: boolean } }
                - { name: count, in: query, schema: { type: integer, format: int32 } }
                - { name: total, in: query, schema: { type: integer, format: int64 } }
                - { name: session, in: cookie, schema: { type: string, pattern: '(' } }
              responses:
                '200': { description: OK., content: { application/json: { schema: { type: object } } } }
                '304': { description: Unchanged., headers: { ETag: { schema: { type: string } }, Age: { schema: { type: integer } } } }
                '401': { description: Unauthorized. }
                '403': { description: Forbidden. }
                '404': { description: Missing. }
                '406': { description: Not acceptable. }
                '410': { description: Gone. }
                '500': { description: Failed. }
                '503': { description: Unavailable. }
            put:
              operationId: ReplaceProject
              requestBody:
                content:
                  multipart/form-data:
                    schema:
                      type: object
                      properties:
                        logo: { type: string, format: binary, minLength: 1, maxLength: 1024 }
                        size: { type: integer }
                        done: { type: boolean }
              responses:
                '204': { description: Replaced. }
                '422': { description: Invalid. }
          /api/v1/projects/{projectId}/notes:
            get:
              operationId: ListNotes
              parameters:
                - { name: projectId, in: path, required: true, schema: { type: string, maxLength: 36 } }
                - { name: limit, in: query, schema: { type: integer, minimum: 1, maximum: 50 } }
              responses:
                '200': { description: OK. }
                '400': { description: Unreadable. }
        components:
          securitySchemes:
            oauth: { type: oauth2, flows: { clientCredentials: { tokenUrl: 'https://auth.example.com/token', scopes: { projects.read: Read., projects.admin: Admin. } } } }
            bearer: { type: http, scheme: bearer }
          schemas:
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
                    id: { type: string, readOnly: true }
                    code: { type: string, minLength: 3, pattern: '.*' }
                    nick: { <<: [*short, *named] }
                    alias: { <<: *short, maxLength: 4 }
                    owner: { type: string, format: email }
                    site: { type: string, format: uri }
                    host: { type: string, format: hostname }
                    ip4: { type: string, format: ipv4 }
                    ip6: { type: string, format: ipv6 }
                    start: { type: string, format: date }
                    at: { type: string, format: date-time }
                    daily: { type: string, format: time }
                    span: { type: string, format: duration }
                    blob: { type: string, format: byte }
                    secret: { type: string, format: password }
                    ref: { type: string, format: uuid }
                    budget: { type: number, minimum: 0.5, maximum: 1000, multipleOf: 0.25 }
                    share: { type: number, exclusiveMinimum: 0, exclusiveMaximum: 1 }
                    weeks: { type: integer, format: int32, maximum: 52, multipleOf: 1 }
                    hours: { type: integer, multipleOf: 5 }
                    priority: { type: integer, enum: [1, 2, 3] }
                    colour: { type: string, enum: [red, orange, yellow, green, blue, indigo] }
                    state: { type: [string, 'null'], enum: [open, null] }
                    kind: { const: project }
                    urgent: { type: boolean }
                    tags: { type: array, minItems: 1, maxItems: 5, uniqueItems: true, items: { type: string, maxLength: 20 } }
                    members: { type: array, minItems: 3, items: { $ref: '#/components/schemas/Member' } }
                    settings: { type: object, minProperties: 3 }
                    plan: { $ref: '#/components/schemas/Step' }
                    contact:
                      oneOf:
                        - { type: object, properties: { email: { type: string, maxLength: 254 } } }
                        - { type: object, properties: { phone: { type: string, maxLength: 20 } } }
                    extra: { anyOf: [{ type: string, maxLength: 10 }, { type: integer }] }
            Member: { type: object, properties: { name: { type: string, maxLength: 40 } } }
            Step: { type: object, properties: { title: { type: string, maxLength: 30 }, next: { $ref: '#/components/schemas/Step' } } }
            Project: { type: object, properties: { name: { type: string } } }
            Problem: { type: object, properties: { title: { type: string } } }
        """;

    private static readonly Lazy<Dictionary<string, JsonElement>> Listed = new(() =>
    {
        var (_, output) = Vacuum.Tool("cases", Write(Contract), "--format", "json");
        using var json = JsonDocument.Parse(output[..(output.LastIndexOf(']') + 1)]);
        return json.RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.Clone(), StringComparer.Ordinal);
    });

    [Theory]
    [InlineData("CreateProject.201", "Send the small example. Expect 201 with the Location header and a Project body.")]
    [InlineData("CreateProject.400", "Send a body the service can't read. Expect 400.")]
    [InlineData("CreateProject.403", "Send credentials without access. Expect 403.")]
    [InlineData("CreateProject.404", "Ask for something that doesn't exist. Expect 404.")]
    [InlineData("CreateProject.409", "Send a request that conflicts with the current state. Expect 409.")]
    [InlineData("CreateProject.422", "Send a body that breaks one of its rules. Expect 422 with a Problem body as application/problem+json.")]
    [InlineData("CreateProject.429", "Go over the rate limit. Expect 429.")]
    [InlineData("GetProject.200", "Send a valid request. Expect 200 with a matching body.")]
    [InlineData("GetProject.304", "Send the ETag of an earlier response as If-None-Match. Expect 304 with the ETag and Age headers.")]
    [InlineData("GetProject.401", "Send no credentials. Expect 401.")]
    [InlineData("GetProject.403", "Send credentials without the projects.read and projects.admin scopes. Expect 403.")]
    [InlineData("GetProject.404", "Use an unknown projectId. Expect 404.")]
    [InlineData("GetProject.410", "Bring about this response. Expect 410.")]
    [InlineData("GetProject.503", "Make a dependency fail. Expect 503.")]
    [InlineData("ReplaceProject.422", "Send a body that breaks one of its rules. Expect 422.")]
    [InlineData("ListNotes.400", "Send a parameter the service can't read. Expect 400.")]
    public void Cases_DocumentedResponse_SaysHowToBringItAbout(string id, string detail) =>
        Assert.Equal(detail, Listed.Value[id].GetProperty("detail").GetString());

    [Theory]
    [InlineData("CreateProject.body.type", "Rejects a list with 400.")]
    [InlineData("CreateProject.body.minProperties", "Accepts 1 field, rejects an empty object with 422.")]
    [InlineData("CreateProject.body.maxProperties", "Accepts 40 fields, rejects 41 with 422.")]
    [InlineData("CreateProject.body.additionalProperties", "Rejects a field the schema doesn't define with 400.")]
    [InlineData("CreateProject.body.name.required", "Rejects a request without name with 422.")]
    [InlineData("CreateProject.body.name.type", "Rejects 42 with 400.")]
    [InlineData("CreateProject.body.name.minLength", "Accepts 1 character, rejects an empty value with 422.")]
    [InlineData("CreateProject.body.name.pattern", "Accepts \"Roof\", rejects \"!\" with 422.")]
    [InlineData("CreateProject.body.code.minLength", "Accepts 3 characters, rejects 2 with 422.")]
    [InlineData("CreateProject.body.code.pattern", "Accepts a value matching .*, rejects a value that breaks it with 422.")]
    [InlineData("CreateProject.body.nick.minLength", "Accepts 2 characters, rejects 1 with 422.")]
    [InlineData("CreateProject.body.nick.maxLength", "Accepts 9 characters, rejects 10 with 422.")]
    [InlineData("CreateProject.body.alias.maxLength", "Accepts 4 characters, rejects 5 with 422.")]
    [InlineData("CreateProject.body.owner.format", "Rejects \"no-at-sign\" with 422.")]
    [InlineData("CreateProject.body.at.format", "Rejects \"2026-02-30T10:00:00Z\" with 422.")]
    [InlineData("CreateProject.body.ref.format", "Rejects \"not-a-uuid\" with 422.")]
    [InlineData("CreateProject.body.budget.type", "Rejects \"abc\" with 400.")]
    [InlineData("CreateProject.body.budget.minimum", "Accepts 0.5, rejects 0.25 with 422.")]
    [InlineData("CreateProject.body.budget.maximum", "Accepts 1000, rejects 1000.25 with 422.")]
    [InlineData("CreateProject.body.budget.multipleOf", "Accepts 0.25, rejects 0.375 with 422.")]
    [InlineData("CreateProject.body.share.exclusiveMinimum", "Accepts 0.01, rejects 0 with 422.")]
    [InlineData("CreateProject.body.share.exclusiveMaximum", "Accepts 0.99, rejects 1 with 422.")]
    [InlineData("CreateProject.body.weeks.maximum", "Accepts 52, rejects 53 with 422.")]
    [InlineData("CreateProject.body.hours.multipleOf", "Accepts 5, rejects 6 with 422.")]
    [InlineData("CreateProject.body.priority.enum", "Accepts 1, 2 and 3, rejects 4 with 422.")]
    [InlineData("CreateProject.body.colour.enum", "Accepts each of its 6 values, rejects \"not-red\" with 422.")]
    [InlineData("CreateProject.body.state.enum", "Accepts \"open\" and null, rejects \"not-open\" with 422.")]
    [InlineData("CreateProject.body.kind.const", "Accepts \"project\", rejects \"not-project\" with 422.")]
    [InlineData("CreateProject.body.urgent.type", "Rejects \"yes\" with 400.")]
    [InlineData("CreateProject.body.tags.type", "Rejects an object with 400.")]
    [InlineData("CreateProject.body.tags.minItems", "Accepts 1 item, rejects an empty list with 422.")]
    [InlineData("CreateProject.body.tags.maxItems", "Accepts 5 items, rejects 6 with 422.")]
    [InlineData("CreateProject.body.tags.uniqueItems", "Rejects two equal items with 422.")]
    [InlineData("CreateProject.body.tags[].maxLength", "Accepts 20 characters, rejects 21 with 422.")]
    [InlineData("CreateProject.body.members.minItems", "Accepts 3 items, rejects 2 with 422.")]
    [InlineData("CreateProject.body.members[].name.maxLength", "Accepts 40 characters, rejects 41 with 422.")]
    [InlineData("CreateProject.body.settings.minProperties", "Accepts 3 fields, rejects 2 with 422.")]
    [InlineData("CreateProject.body.plan.title.maxLength", "Accepts 30 characters, rejects 31 with 422.")]
    [InlineData("CreateProject.body.contact.oneOf", "Rejects a value for contact that matches none of its 2 variants with 422.")]
    [InlineData("CreateProject.body.contact.phone.maxLength", "Accepts 20 characters, rejects 21 with 422.")]
    [InlineData("CreateProject.body.extra.anyOf", "Rejects a value for extra that matches none of its 2 variants with 422.")]
    [InlineData("GetProject.path.projectId.format", "Rejects \"not-a-uuid\" with 422.")]
    [InlineData("GetProject.header.X-Trace.required", "Rejects a request without X-Trace with 400.")]
    [InlineData("GetProject.header.X-Trace.minLength", "Accepts 3 characters, rejects 2 with 422.")]
    [InlineData("GetProject.header.If-None-Match.pattern", "Accepts \"\\\"7\\\"\", rejects \"!\" with 422.")]
    [InlineData("GetProject.query.archived.type", "Rejects maybe with 400.")]
    [InlineData("GetProject.query.count.type", "Rejects abc with 400.")]
    [InlineData("GetProject.query.count.format", "Rejects 2147483648 (past int32) with 422.")]
    [InlineData("GetProject.query.total.format", "Rejects 9223372036854775808 (past int64) with 422.")]
    [InlineData("GetProject.cookie.session.pattern", "Accepts a value matching (, rejects a value that breaks it with 422.")]
    [InlineData("ReplaceProject.header.X-Trace.maxLength", "Accepts 64 characters, rejects 65 with 422.")]
    [InlineData("ReplaceProject.body.logo.minLength", "Accepts 1 byte, rejects an empty value with 422.")]
    [InlineData("ReplaceProject.body.logo.maxLength", "Accepts 1024 bytes, rejects 1025 with 422.")]
    [InlineData("ReplaceProject.body.size.type", "Rejects abc with 422.")]
    [InlineData("ListNotes.path.projectId.maxLength", "Accepts 36 characters, rejects 37 with 400.")]
    [InlineData("ListNotes.query.limit.minimum", "Accepts 1, rejects 0 with 400.")]
    public void Cases_InputRule_SaysWhatIsAcceptedAndRejected(string id, string detail) =>
        Assert.Equal(detail, Listed.Value[id].GetProperty("detail").GetString());

    // A path parameter is always sent, a read-only field is the service's, a password has no format to break,
    // an int32 with a maximum is bounded by it, every integer is a multiple of 1, and the text body isn't JSON.
    [Theory]
    [InlineData("GetProject.path.projectId.required")]
    [InlineData("CreateProject.body.id.type")]
    [InlineData("CreateProject.body.secret.format")]
    [InlineData("CreateProject.body.weeks.format")]
    [InlineData("CreateProject.body.weeks.multipleOf")]
    [InlineData("CreateProject.body.colour.maxLength")]
    [InlineData("ReplaceProject.body.logo.type")]
    [InlineData("CreateProject.body.plan.next.next.title.maxLength")]
    public void Cases_RuleThatCantFail_IsNotListed(string id) =>
        Assert.DoesNotContain(id, Listed.Value.Keys);

    [Fact]
    public void Cases_Json_GivesEachCaseItsOperationAndStatus()
    {
        var found = Listed.Value["GetProject.query.count.format"];

        Assert.Equal(
            ("GetProject", "GET", "/api/v1/projects/{projectId}", "422"),
            (found.GetProperty("operation").GetString(), found.GetProperty("method").GetString(),
                found.GetProperty("path").GetString(), found.GetProperty("status").GetString()));
    }

    [Fact]
    public void Cases_Text_ListsEachOperationUnderItsMethodAndPath()
    {
        var (exitCode, output) = Vacuum.Tool("cases", Write(Contract));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "ListNotes  GET /api/v1/projects/{projectId}/notes\n  ListNotes.200                       Send a valid request.",
            output.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Contains($"{Listed.Value.Count} cases in 4 operations", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("responses", "CreateProject.201", "CreateProject.body.type")]
    [InlineData("rules", "CreateProject.body.type", "CreateProject.201")]
    public void Cases_Only_ListsOneKind(string only, string listed, string left)
    {
        var output = Vacuum.Tool("cases", Write(Contract), "--only", only).Output;

        Assert.Contains($"  {listed} ", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"  {left} ", output, StringComparison.Ordinal);
    }

    // The example contract leaves the service to say a header is missing (400) and a value is wrong (422).
    [Fact]
    public void Cases_ExampleContract_SplitsUnreadableFromInvalid()
    {
        var output = Vacuum.Tool("cases", Path.Combine(AppContext.BaseDirectory, "examples", "openapi.yaml")).Output;

        Assert.Contains("CreateBooking.header.Idempotency-Key.required   Rejects a request without Idempotency-Key with 400.", output, StringComparison.Ordinal);
        Assert.Contains("CreateBooking.header.Idempotency-Key.format     Rejects \"not-a-uuid\" with 422.", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--colour", "red", "unknown option '--colour'")]
    [InlineData("--format", "xml", "--format takes text or json")]
    [InlineData("--only", null, "--only takes responses or rules")]
    public void Cases_BadOption_ExitsTwo(string option, string? value, string message)
    {
        string[] args = ["cases", Write(Contract), option, .. value is null ? [] : new[] { value }];

        var (exitCode, output) = Vacuum.Tool(args);

        Assert.Equal((2, true), (exitCode, output.Contains(message, StringComparison.Ordinal)));
    }

    [Fact]
    public void Cases_NoFile_ExitsTwo()
    {
        var (exitCode, output) = Vacuum.Tool("cases", "missing.yaml");

        Assert.Equal((2, true), (exitCode, output.Contains("no file at missing.yaml", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("paths: [1, 2\n", "did not find expected ',' or ']'")]
    [InlineData("", "the file holds no YAML or JSON object")]
    [InlineData("just text\n", "the file holds no YAML or JSON object")]
    [InlineData("openapi: 9.9.9\n", "OpenAPI specification version '9.9.9' is not supported")]
    [InlineData("info: { title: T, version: v1 }\n", "Version node not found.")]
    [InlineData("openapi: 3.1.1\npaths: []\n", "The field 'title' in 'info' object is REQUIRED.")]
    [InlineData("openapi: 3.1.1\ninfo: { title: T, version: v1 }\ncomponents: { schemas: { A: { minLength: abc } } }\n", "a schema keyword that takes a number has something else")]
    public void Cases_UnreadableContract_ExitsOneAndSaysWhy(string yaml, string message)
    {
        var (exitCode, output) = Vacuum.Tool("cases", Write(yaml));

        Assert.Equal((1, true), (exitCode, output.Contains(message, StringComparison.Ordinal)));
    }

    // "api" and the version drop out, a parameter reads as "by", and a name another operation has gets a number.
    [Fact]
    public void Cases_NoOperationId_NamesTheCasesByMethodAndRoute()
    {
        const string unnamed = """
            openapi: 3.1.1
            info: { title: Notes, version: v1 }
            paths:
              '/api/v1.0/notes/{noteId}:archive': { post: { responses: { '204': { description: Archived. } } } }
              /api/v1/notes: { get: { responses: { '200': { description: OK. } } } }
              /api/v2/notes: { get: { responses: { '200': { description: OK. } } } }
              /notes: { get: { operationId: GetNotes, responses: { '200': { description: OK. } } } }
            """;

        var (exitCode, output) = Vacuum.Tool("cases", Write(unnamed));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "3 operations without an operationId: each case ID starts with a name made from the method and route "
                + "instead, such as PostNotesByNoteIdArchive",
            output,
            StringComparison.Ordinal);
        Assert.All(
            ["PostNotesByNoteIdArchive.204", "GetNotes2.200", "GetNotes3.200", "GetNotes.200"],
            id => Assert.Contains($"  {id}  ", output, StringComparison.Ordinal));
    }

    // Swagger 2.0 has a body only where a response has a schema.
    [Fact]
    public void Cases_Swagger2_ExpectsABodyOnlyWithASchema()
    {
        const string swagger = """
            swagger: '2.0'
            info: { title: Notes, version: v1 }
            produces: [application/json]
            paths:
              /notes:
                get:
                  operationId: ListNotes
                  responses:
                    '200': { description: OK., schema: { type: array, items: { type: string } } }
                    '404': { description: Missing. }
            """;

        var output = Vacuum.Tool("cases", Write(swagger)).Output;

        Assert.Contains("ListNotes.200  Send a valid request. Expect 200 with a matching body.", output, StringComparison.Ordinal);
        Assert.Contains("ListNotes.404  Ask for something that doesn't exist. Expect 404.", output, StringComparison.Ordinal);
        Assert.Contains("2 cases in 1 operation\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static string Write(string yaml)
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, yaml);
        return file;
    }
}
