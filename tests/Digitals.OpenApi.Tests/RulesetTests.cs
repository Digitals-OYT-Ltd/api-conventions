namespace Digitals.OpenApi.Tests;

/// <summary>
/// Each test edits a document that passes every rule and checks exactly which rules then fire.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RulesetTests
{
    // One operation that follows every rule, in the YAML a designer writes by hand.
    private const string Clean = $$"""
        openapi: 3.1.1
        info: { title: Projects, version: v1, description: Projects for teams. A project records the milestones and tasks a team has agreed to deliver. }
        tags: [{ name: Projects, description: Create and read projects and move them through their lifecycle. }]
        security: [{ bearer: [] }]
        paths:
          /api/v1/projects/{projectId}:archive:
            post:
              tags: [Projects]
              summary: Archives a project.
              description: Moves the project to archived so it no longer appears in lists. It can still be read by id.
              operationId: ArchiveProject
              parameters:
                - { name: projectId, in: path, description: The project's id., required: true, example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: { type: string, format: uuid, maxLength: 36 } }
              requestBody: { description: Why the project is being archived., content: { application/json: { schema: { $ref: '#/components/schemas/Archive' } } } }
              responses:
                {{ProjectResponse}}
                {{UnauthorizedResponse}}
                {{ProblemResponse}}
                '429': { $ref: '#/components/responses/TooManyRequests' }
                '500': { $ref: '#/components/responses/Failed' }
        components:
          headers:
            RateLimit-Policy: { description: The caller's quota of q requests per window of w seconds., schema: { type: string, maxLength: 128, pattern: '^"[a-z0-9-]+";q=\d+;w=\d+$' }, example: '"standard";q=100;w=60' }
            RateLimit: { description: Requests left in this window (r) and seconds until it resets (t)., schema: { type: string, maxLength: 128, pattern: '^"[a-z0-9-]+";r=\d+;t=\d+$' }, example: '"standard";r=42;t=17' }
            Retry-After: { description: Seconds to wait before trying again., schema: { type: integer, format: int32, minimum: 0, maximum: 86400 }, example: 17 }
          responses:
            TooManyRequests: { description: Too many requests from this caller in the current window., headers: { RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' }, Retry-After: { $ref: '#/components/headers/Retry-After' } }, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Too Many Requests, status: 429 } } } }
            Failed: { description: The service failed., content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: An error occurred while processing your request., status: 500 } } } }
          securitySchemes:
            bearer: { type: http, scheme: bearer, bearerFormat: JWT, description: An access token (JWT) from the identity provider. }
          schemas:
            Archive: { type: object, additionalProperties: false, properties: { reason: { type: string, maxLength: 500, pattern: '^[^\u0000-\u001F]*$', description: Why the project was archived. Shown to people who open it later. } }, example: { reason: Replaced by a new project after the review. }, description: What a client sends to archive a project. }
            Project: { type: object, properties: { projectId: { type: string, format: uuid, maxLength: 36, description: Assigned by the service when the project is created. }, status: { $ref: '#/components/schemas/Status', description: Where the project is in its lifecycle. }, createTime: { type: string, format: date-time, maxLength: 33, description: When the project was created in UTC. } }, example: { projectId: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, status: active, createTime: '2026-10-29T09:30:00Z' }, description: The milestones and tasks one team has agreed to deliver. }
            Status: { type: string, enum: [active, archived], example: active, description: Active while the team works on it. Archived once it no longer applies. }
            ProblemDetails: { type: object, properties: { title: { type: [ 'null', string ], maxLength: 4096, pattern: '^[^\u0000-\u001F]*$', description: A short summary of the kind of error. } }, example: { title: Not Found }, description: An error response in RFC 9457 problem details format. }
        """;

    // The RateLimit headers every 2xx and 4xx response carries.
    private const string RateLimited = "headers: { RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' } }";
    private const string BoundedString = "{ type: string, maxLength: 20, pattern: '^[a-z]*$' }";
    private const string DescribedString = "{ type: string, maxLength: 20, pattern: '^[a-z]*$', description: Added by this test. }";
    private const string ProjectResponse = $$"""'200': { description: OK, {{RateLimited}}, content: { application/json: { schema: { $ref: '#/components/schemas/Project' } } } }""";
    private const string UnauthorizedResponse = $$"""'401': { description: Unauthorized, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Unauthorized, status: 401 } } } }""";
    private const string ProblemResponse = $$"""'404': { description: Not Found, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Not Found, status: 404 } } } }""";

    private const string Baseline = "digitals-baseline.yaml";

    // A project that can be read and deleted, with the version check both ends need.
    private const string Resource = $$"""
          /api/v1/projects/{projectId}:
            get:
              tags: [Projects]
              summary: Gets a project.
              description: Returns the project with its version in the ETag header. Send that version in If-Match to change it.
              operationId: GetProject
              parameters:
                - { name: projectId, in: path, description: The project's id., required: true, example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: { type: string, format: uuid, maxLength: 36 } }
              responses:
                '200': { description: OK, headers: { ETag: { description: The project's current version., example: '"3"', schema: { type: string, maxLength: 64, pattern: '^"[^"]*"$' } }, RateLimit-Policy: { $ref: '#/components/headers/RateLimit-Policy' }, RateLimit: { $ref: '#/components/headers/RateLimit' } }, content: { application/json: { schema: { $ref: '#/components/schemas/Project' } } } }
                {{UnauthorizedResponse}}
                '429': { $ref: '#/components/responses/TooManyRequests' }
                '500': { $ref: '#/components/responses/Failed' }
            delete:
              tags: [Projects]
              summary: Deletes a project.
              description: Deletes the project if it hasn't changed since the caller read it. Answers 412 when it has.
              operationId: DeleteProject
              parameters:
                - { name: projectId, in: path, description: The project's id., required: true, example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: { type: string, format: uuid, maxLength: 36 } }
                - { name: If-Match, in: header, description: The ETag from the caller's last read., required: true, example: '"3"', schema: { type: string, maxLength: 64, pattern: '^"[^"]*"$' } }
              responses:
                '204': { description: No Content, {{RateLimited}} }
                {{UnauthorizedResponse}}
                '412': { description: Precondition Failed, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Precondition Failed, status: 412 } } } }
                '428': { description: Precondition Required, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Precondition Required, status: 428 } } } }
                '429': { $ref: '#/components/responses/TooManyRequests' }
                '500': { $ref: '#/components/responses/Failed' }

        """;

    // A create on the projects collection that a client can retry with the same key.
    private const string Create = $$"""
          /api/v1/projects:
            post:
              tags: [Projects]
              summary: Creates a project.
              description: Creates a project. Send an Idempotency-Key to retry safely.
              operationId: CreateProject
              parameters:
                - { name: Idempotency-Key, in: header, description: A UUID the client makes up for each create. A retry with the same key gets the first response., required: true, example: 5b0d7a3e-2c4f-4e8a-9f61-0c9b7d2e1a44, schema: { type: string, format: uuid, maxLength: 36 } }
              requestBody: { description: Why the project is being archived., content: { application/json: { schema: { $ref: '#/components/schemas/Archive' } } } }
              responses:
                '201': { description: Created, {{RateLimited}}, content: { application/json: { schema: { $ref: '#/components/schemas/Project' } } } }
                '400': { description: Bad Request, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Bad Request, status: 400 } } } }
                {{UnauthorizedResponse}}
                '422': { description: Unprocessable Entity, {{RateLimited}}, content: { application/problem+json: { schema: { $ref: '#/components/schemas/ProblemDetails' }, example: { title: Unprocessable Entity, status: 422 } } } }
                '429': { $ref: '#/components/responses/TooManyRequests' }
                '500': { $ref: '#/components/responses/Failed' }

        """;

    [Fact]
    public void Lint_ConventionalDocument_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Clean));

    // examples/openapi.yaml uses every part of OpenAPI 3.1 Swagger UI shows: webhooks, links, oneOf and the rest.
    [Fact]
    public void Lint_FullExample_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "openapi.yaml"))));

    [Theory]
    [InlineData("/api/projects", "dt-path-version")]
    [InlineData("/api/v1.0/projects", "dt-path-version")]
    [InlineData("/api/v0/projects", "dt-path-version")]
    [InlineData("/api/v01/projects", "dt-path-version")]
    [InlineData("/api/v1/v2/projects", "dt-path-version")]
    [InlineData("/api/v1/projectTemplates", "dt-path-segments")]
    [InlineData("/api/v1/project_templates", "dt-path-segments")]
    [InlineData("/api/v1/projects/{projectId}:Archive", "dt-path-segments")]
    [InlineData("/api/v1/team-project-templates", "dt-path-word-count")]
    [InlineData("/api/v1/projects/{projectId}:archive-by-owner", "dt-path-word-count")]
    [InlineData("/api/v1/projects/get", "no-http-verbs-in-path")]
    [InlineData("/api/v1/projects/{projectId}/delete", "no-http-verbs-in-path")]
    [InlineData("/api/v1/projects/{projectId}:delete", "dt-no-http-verb-action")]
    [InlineData("/api/v1/projects:get", "dt-no-http-verb-action")]
    [InlineData("/api/v1/teams/{teamId}/projects/{projectId}/tasks/{taskId}/notes/{noteId}", "dt-path-depth")]
    [InlineData("/api/v1/projects/{id}", "dt-path-id")]
    [InlineData("/api/v1/submissions/{id}/documents/{documentId}", "dt-path-id")]
    [InlineData("/api/v1/submissions/{submissionId}/documents/{document}", "dt-path-id")]
    public void Lint_UnconventionalPath_Reports(string path, string expected) =>
        Assert.Equal([expected], Vacuum.Lint(WithPath(path)));

    [Fact]
    public void Lint_TemplatedPathBeforeLiteral_Reports() =>
        Assert.Equal(
            ["paths-specificity-order"],
            Vacuum.Lint(Edit(WithPath("/api/v1/projects/archived"), "paths:\n", "paths:\n" + Resource)));

    [Fact]
    public void Lint_LiteralPathBeforeTemplated_FindsNothing()
    {
        const string archive = "  /api/v1/projects/{projectId}:archive:\n";

        Assert.Empty(Vacuum.Lint(Edit(WithPath("/api/v1/projects/archived"), archive, Resource + archive)));
    }

    [Theory]
    [InlineData("/api/v1/projects:validate")]
    [InlineData("/api/v2/notes/{noteId}")]
    [InlineData("/api/v1")]
    [InlineData("/v1/projects")]
    [InlineData("/api/v1/projects/{projectId}:batch-get")]
    [InlineData("/billing/v12/projects")]
    [InlineData("/api/v1/teams/{teamId}/projects/{projectId}/tasks/{taskId}:archive")]
    [InlineData("/api/v1/submissions/{submissionId}/documents/{documentId}/preview")]
    public void Lint_ConventionalPath_FindsNothing(string path) =>
        Assert.Empty(Vacuum.Lint(WithPath(path)));

    [Fact]
    public void Lint_PascalCaseQueryParameter_Reports() =>
        Assert.Equal(["dt-parameter-casing"], Vacuum.Lint(WithQuery("PageSize")));

    [Fact]
    public void Lint_HeaderParameterWithDashes_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(WithParameter($"{{ name: X-Request-Id, in: header, description: Added by this test., example: abc, schema: {BoundedString} }}")));

    [Theory]
    [InlineData("niNumber")]
    [InlineData("dateOfBirth")]
    [InlineData("postcode")]
    public void Lint_PersonalDataInQuery_Reports(string name) =>
        Assert.Equal(["dt-no-personal-data-in-url"], Vacuum.Lint(WithQuery(name)));

    [Fact]
    public void Lint_PersonalDataInSnakeCase_ReportsBoth() =>
        Assert.Equal(["dt-no-personal-data-in-url", "dt-parameter-casing"], Vacuum.Lint(WithQuery("date_of_birth")));

    // vacuum counts any parameter containing "token" as a credential, so Google-style paging fails.
    [Fact]
    public void Lint_PageTokenQuery_Reports() =>
        Assert.Equal(["owasp-no-credentials-in-url"], Vacuum.Lint(WithQuery("pageToken")));

    [Fact]
    public void Lint_ParameterWithoutDescription_Reports() =>
        Assert.Equal(
            ["oas3-parameter-description"],
            Vacuum.Lint(Edit(Clean, "in: path, description: The project's id., ", "in: path, ")));

    [Fact]
    public void Lint_PathLevelParameter_IsChecked()
    {
        var yaml = Edit(Clean, "    post:\n", $"    parameters:\n      - {{ name: PageSize, in: query, description: Added by this test., example: abc, schema: {BoundedString} }}\n    post:\n");

        Assert.Equal(["dt-parameter-casing"], Vacuum.Lint(yaml));
    }

    [Fact]
    public void Lint_NoTags_Reports() =>
        Assert.Equal(["operation-tags"], Vacuum.Lint(Edit(Clean, "      tags: [Projects]\n", "")));

    [Fact]
    public void Lint_NoOperationId_Reports() =>
        Assert.Equal(["operation-operationId"], Vacuum.Lint(Edit(Clean, "      operationId: ArchiveProject\n", "")));

    [Fact]
    public void Lint_BlankOperationId_Reports() =>
        Assert.Equal(
            ["dt-operation-id-casing", "operation-operationId"],
            Vacuum.Lint(Edit(Clean, "operationId: ArchiveProject", "operationId: ''")));

    [Fact]
    public void Lint_DuplicateOperationId_Reports()
    {
        var yaml = Edit(WithPath("/api/v1/projects"), "operationId: GetExtra", "operationId: ArchiveProject");

        Assert.Equal(["operation-operationId-unique"], Vacuum.Lint(yaml));
    }

    [Theory]
    [InlineData("archiveProject")]
    [InlineData("Archive_Project")]
    [InlineData("Projects.Archive")]
    [InlineData("archive-project")]
    [InlineData("ArchiveProjectAsync")]
    public void Lint_OperationIdNotPascalCase_Reports(string id) =>
        Assert.Equal(
            ["dt-operation-id-casing"],
            Vacuum.Lint(Edit(Clean, "operationId: ArchiveProject", $"operationId: {id}")));

    [Fact]
    public void Lint_NoSummary_Reports() =>
        Assert.Equal(["dt-operation-summary"], Vacuum.Lint(Edit(Clean, "      summary: Archives a project.\n", "")));

    [Fact]
    public void Lint_ErrorAsPlainJson_Reports() =>
        Assert.Equal(
            ["dt-problem-json"],
            Vacuum.Lint(Edit(Clean, "content: { application/problem+json:", "content: { application/json:")));

    [Fact]
    public void Lint_ErrorWithoutBody_Reports()
    {
        var yaml = Edit(Clean, ProblemResponse, $"'404': {{ description: Not Found, {RateLimited} }}");

        Assert.Equal(["dt-problem-json"], Vacuum.Lint(yaml));
    }

    [Fact]
    public void Lint_NoContentAndRedirect_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Edit(Clean, "      responses:\n", $"      responses:\n        '204': {{ description: No Content, {RateLimited} }}\n        '302': {{ description: Found }}\n")));

    [Fact]
    public void Lint_ResponseWithoutRateLimitHeaders_Reports() =>
        Assert.Equal(["owasp-rate-limit"], Vacuum.Lint(
            Edit(Clean, ProjectResponse, ProjectResponse.Replace(RateLimited + ", ", "", StringComparison.Ordinal))));

    [Fact]
    public void Lint_TooManyRequestsWithoutRetryAfter_Reports() =>
        Assert.Equal(
            ["owasp-rate-limit-retry-after"],
            Vacuum.Lint(Edit(Clean, "Retry-After: { $ref", "Retry-In: { $ref")));

    // The renamed response keeps its component, so the component stays used and its example's status is wrong.
    [Theory]
    [InlineData("'429': { $ref", "'409': { $ref", "owasp-define-error-responses-429")]
    [InlineData("'500': { $ref", "'503': { $ref", "owasp-define-error-responses-500")]
    public void Lint_MissingFailureResponse_Reports(string find, string replacement, string expected) =>
        Assert.Equal(["dt-error-example", expected], Vacuum.Lint(Edit(Clean, find, replacement)));

    // Component schema, parameter, header.
    [Theory]
    [InlineData(", example: active,", ",")]
    [InlineData("example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema:", "schema:")]
    [InlineData(", example: 17 }", " }")]
    public void Lint_MissingExample_Reports(string find, string replacement) =>
        Assert.Equal(["dt-example"], Vacuum.Lint(Edit(Clean, find, replacement)));

    // A component schema's example, a body's example and a named example, each broken one way. vacuum's
    // own check sees a component schema example of the wrong type or outside its enum, and nothing else.
    [Theory]
    [InlineData("example: { projectId: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73", "example: { projectId: 42")]
    [InlineData("status: active, createTime", "status: closed, createTime")]
    public void Lint_InvalidSchemaExample_ReportsBoth(string find, string replacement) =>
        Assert.Equal(["dt-example-valid", "oas3-valid-schema-example"], Vacuum.Lint(Edit(Clean, find, replacement)));

    [Theory]
    [InlineData("createTime: '2026-10-29T09:30:00Z'", "createTime: yesterday")]
    [InlineData("example: { reason: Replaced", "example: { colour: red, reason: Replaced")]
    [InlineData("example: { title: Not Found, status: 404 }", "example: { title: 7, status: 404 }")]
    [InlineData(
        "example: { title: Not Found, status: 404 }",
        "examples: { gone: { value: { title: [7], status: 404 } } }")]
    public void Lint_InvalidExample_Reports(string find, string replacement)
    {
        var yaml = Edit(Clean, find, replacement);
        Assert.Equal(["dt-example-valid"], Vacuum.Lint(yaml));
        Assert.Equal(["dt-example-valid"], Vacuum.Lint(yaml, Baseline));
    }

    [Fact]
    public void Lint_ValidNamedExamples_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Edit(Clean, "example: { title: Not Found, status: 404 }", "examples: { gone: { value: { title: Not Found, status: 404 } }, unnamed: { value: { title: null, status: 404 } } }")));

    [Fact]
    public void Lint_InlineBodyWithoutExample_Reports() =>
        Assert.Equal(["dt-error-example", "dt-example"], Vacuum.Lint(Edit(Clean, ProblemResponse, $$"""'404': { description: Not Found, {{RateLimited}}, content: { application/problem+json: { schema: { type: object, description: An error response in RFC 9457 problem details format., properties: { title: { type: string, maxLength: 20, pattern: '^[a-z]*$', description: Added by this test. } } } } } }""")));

    [Fact]
    public void Lint_InlineArrayResponse_Reports()
    {
        var yaml = Edit(Clean, ProjectResponse, $$"""'200': { description: OK, {{RateLimited}}, content: { application/json: { schema: { type: array, maxItems: 10, items: { $ref: '#/components/schemas/Project' }, example: [] } } } }""");

        Assert.Equal(["dt-no-bare-array-response"], Vacuum.Lint(yaml));
    }

    [Fact]
    public void Lint_ReferencedNullableArrayResponse_Reports()
    {
        var yaml = Edit(
            Edit(
                Clean,
                ProjectResponse,
                ProjectResponse.Replace("Project'", "Projects'", StringComparison.Ordinal)),
            "    Status:",
            "    Projects: { type: [ 'null', array ], maxItems: 10, items: { $ref: '#/components/schemas/Project' }, example: [], description: One page of projects. }\n    Status:");

        Assert.Equal(["dt-no-bare-array-response"], Vacuum.Lint(yaml));
    }

    // A summary doesn't stand in for an operation's description.
    [Theory]
    [InlineData("      description: Moves the project to archived so it no longer appears in lists. It can still be read by id.\n", "")]
    [InlineData("requestBody: { description: Why the project is being archived., ", "requestBody: { ")]
    [InlineData(", description: What a client sends to archive a project. }", " }")]
    [InlineData(", description: Why the project was archived. Shown to people who open it later. }", " }")]
    [InlineData(", description: An access token (JWT) from the identity provider. }", " }")]
    public void Lint_MissingDescription_Reports(string find, string replacement) =>
        Assert.Equal(["dt-description"], Vacuum.Lint(Edit(Clean, find, replacement)));

    // vacuum follows the reference, so the component's description covers the property.
    [Fact]
    public void Lint_ReferencedPropertyWithoutDescription_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Edit(
            Clean,
            "'#/components/schemas/Status', description: Where the project is in its lifecycle. }",
            "'#/components/schemas/Status' }")));

    [Fact]
    public void Lint_MissingDocumentDescription_Reports() =>
        Assert.Equal(["info-description"], Vacuum.Lint(Edit(Clean, ", description: Projects for teams. A project records the milestones and tasks a team has agreed to deliver. }", " }")));

    [Fact]
    public void Lint_MissingTagDescription_Reports() =>
        Assert.Equal(["tag-description"], Vacuum.Lint(
            Edit(Clean, ", description: Create and read projects and move them through their lifecycle. }", " }")));

    [Fact]
    public void Lint_PascalCaseProperty_Reports() =>
        Assert.Equal(["dt-property-casing"], Vacuum.Lint(Edit(Clean, "createTime:", "CreateTime:")));

    [Fact]
    public void Lint_NestedPascalCaseProperty_Reports()
    {
        var yaml = Edit(
            Edit(Clean, "reason: { type: string,", $"reason: {{ type: object, additionalProperties: false, description: Added by this test., properties: {{ Detail: {DescribedString} }} }}, unused: {{ type: string,"),
            "example: { reason: Replaced by a new project after the review. }",
            "example: { reason: { Detail: replaced } }");

        Assert.Equal(["dt-property-casing"], Vacuum.Lint(yaml));
    }

    [Theory]
    [InlineData("Archived")]
    [InlineData("ARCHIVED")]
    [InlineData("on_hold")]
    [InlineData("on-hold")]
    public void Lint_EnumValueNotCamelCase_Reports(string value) =>
        Assert.Equal(
            ["dt-enum-casing"],
            Vacuum.Lint(Edit(Clean, "enum: [active, archived]", $"enum: [active, {value}]")));

    [Fact]
    public void Lint_IntegerEnumSchema_Reports()
    {
        var yaml = Edit(
            Edit(
                Clean,
                "Status: { type: string, enum: [active, archived], example: active",
                "Status: { type: integer, format: int32, minimum: 0, maximum: 1, enum: [0, 1], example: 0"),
            "status: active",
            "status: 0");

        Assert.Equal(["dt-no-integer-enum"], Vacuum.Lint(yaml));
    }

    [Theory]
    [InlineData("createdAt: { type: string, format: date-time, maxLength: 33 }", "dt-timestamp-name")]
    [InlineData("birthDay: { type: string, format: date, maxLength: 10 }", "dt-date-name")]
    [InlineData("dueDate: { type: string, maxLength: 10, pattern: '^[0-9-]*$' }", "dt-date-suffix-format")]
    [InlineData("openingDate: { type: string, format: time, maxLength: 16 }", "dt-date-suffix-format")]
    [InlineData("startTime: { type: string, maxLength: 33, pattern: '^[0-9T:.Z-]*$' }", "dt-time-suffix-format")]
    public void Lint_TemporalNameMismatch_Reports(string property, string expected) =>
        Assert.Equal([expected], Vacuum.Lint(WithProperty(property)));

    [Fact]
    public void Lint_TimeNamedDate_ReportsBothSides() =>
        Assert.Equal(
            ["dt-date-name", "dt-time-suffix-format"],
            Vacuum.Lint(WithProperty("startTime: { type: string, format: date, maxLength: 10 }")));

    [Theory]
    [InlineData("updateTime: { type: [ 'null', string ], format: date-time, maxLength: 33 }")]
    [InlineData("birthDate: { type: string, format: date, maxLength: 10 }")]
    [InlineData("date: { type: string, format: date, maxLength: 10 }")]
    [InlineData("openingTime: { type: string, format: time, maxLength: 16 }")]
    public void Lint_TemporalNameMatches_FindsNothing(string property) =>
        Assert.Empty(Vacuum.Lint(WithProperty(property)));

    // A field that takes its format or type from a YAML anchor is checked on what the anchor holds.
    [Theory]
    [InlineData("dueDate: { <<: *date, description: When it's due. }")]
    [InlineData("retentionDays: { <<: *days, description: How long it's kept. }")]
    public void Lint_FormatFromAnchor_FindsNothing(string property) =>
        Assert.Empty(Vacuum.Lint(Edit(
            Clean,
            "status: { $ref",
            "startDate: &date { type: string, format: date, maxLength: 10, description: When it starts. }, keepDays: &days { type: integer, format: int32, minimum: 0, maximum: 3650, description: How long it's kept. }, " + property + ", status: { $ref")));

    [Theory]
    [InlineData("timeoutSeconds: { type: string, maxLength: 10, pattern: '^[0-9]*$' }", "dt-unit-integer")]
    [InlineData("retentionDays: { type: number, format: double, minimum: 0, maximum: 3650 }", "dt-unit-integer")]
    [InlineData("ttl: { type: integer, format: int32, minimum: 0, maximum: 86400 }", "dt-unit-name")]
    [InlineData("timeoutMillis: { type: integer, format: int32, minimum: 0, maximum: 86400 }", "dt-unit-name")]
    [InlineData("cycleWeeks: { type: integer, format: int32, minimum: 1, maximum: 52 }", "dt-unit-name")]
    [InlineData("termMonths: { readOnly: true }", "dt-unit-integer")]
    [InlineData(
        "wait: { type: string, maxLength: 26, pattern: '^-?(\\d+\\.)?\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?$' }",
        "dt-no-duration-string")]
    [InlineData("wait: { type: string, format: duration, maxLength: 32 }", "dt-no-duration-string")]
    public void Lint_DurationNotIntegerUnit_Reports(string property, string expected) =>
        Assert.Equal([expected], Vacuum.Lint(WithProperty(property)));

    [Theory]
    [InlineData("timeoutSeconds: { type: integer, format: int32, minimum: 1, maximum: 3600 }")]
    [InlineData("retentionDays: { type: [ integer, 'null' ], format: int32, minimum: 0, maximum: 3650 }")]
    [InlineData("retentionDays: { type: [ 'null', integer ], format: int32, minimum: 0, maximum: 3650 }")]
    [InlineData("termMonths: { type: integer, format: int32, minimum: 1, maximum: 120 }")]
    [InlineData("fileSizeBytes: { type: integer, format: int64, minimum: 0, maximum: 1073741824 }")]
    [InlineData("holidays: { type: string, maxLength: 10, pattern: '^[a-z]*$' }")]
    public void Lint_DurationIntegerUnit_FindsNothing(string property) =>
        Assert.Empty(Vacuum.Lint(WithProperty(property)));

    [Fact]
    public void Lint_StringWithoutMaxLength_Reports() =>
        Assert.Equal(
            ["dt-string-limit"],
            Vacuum.Lint(Edit(Clean, "reason: { type: string, maxLength: 500, ", "reason: { type: string, ")));

    [Fact]
    public void Lint_StringWithoutFormatOrPattern_Reports() =>
        Assert.Equal(["owasp-string-restricted"], Vacuum.Lint(Edit(
            Clean,
            "reason: { type: string, maxLength: 500, pattern: '^[^\\u0000-\\u001F]*$',",
            "reason: { type: string, maxLength: 500,")));

    [Fact]
    public void Lint_UnboundedParameter_Reports() =>
        Assert.Equal(
            ["dt-string-limit"],
            Vacuum.Lint(Edit(Clean, "format: uuid, maxLength: 36 } }\n", "format: uuid } }\n")));

    [Fact]
    public void Lint_RequestArrayWithoutMaxItems_Reports() =>
        Assert.Equal(["dt-array-limit"], Vacuum.Lint(Edit(Clean, "reason: { type: string,", "labels: { type: array, items: { type: string, enum: [urgent] }, description: Added by this test. }, reason: { type: string,")));

    [Theory]
    [InlineData("labels: { type: array, items: { type: string, enum: [urgent] } }", "dt-array-limit")]
    [InlineData(
        "labels: { type: array, maxItems: 5, items: { type: string, pattern: '^[a-z]*$' } }",
        "dt-string-limit")]
    [InlineData("label: { type: [ 'null', string ], pattern: '^[a-z]*$' }", "dt-string-limit")]
    [InlineData("owner: { type: object, properties: { label: { type: string, pattern: '^[a-z]*$', description: Added by this test. } } }", "dt-string-limit")]
    public void Lint_UnboundedResponseField_Reports(string property, string expected) =>
        Assert.Equal([expected], Vacuum.Lint(WithProperty(property)));

    [Fact]
    public void Lint_UnboundedInlineResponse_Reports() =>
        Assert.Equal(["dt-string-limit"], Vacuum.Lint(Edit(Clean, ProblemResponse, $$"""'404': { description: Not Found, {{RateLimited}}, content: { application/problem+json: { example: { title: abc, status: 404 }, schema: { type: object, description: An error response in RFC 9457 problem details format., properties: { title: { type: string, pattern: '^[a-z]*$', description: Added by this test. } } } } } }""")));

    [Fact]
    public void Lint_UnboundedResponseHeader_Reports() =>
        Assert.Equal(["dt-string-limit"], Vacuum.Lint(Edit(Clean, "'200': { description: OK, headers: { ", "'200': { description: OK, headers: { ETag: { description: Added by this test., example: abc, schema: { type: string, pattern: '^[a-z]*$' } }, ")));

    [Theory]
    [InlineData("label: { type: string, const: draft }")]
    [InlineData("labels: { type: array, maxItems: 5, items: { $ref: '#/components/schemas/Status' } }")]
    public void Lint_BoundedResponseField_FindsNothing(string property) =>
        Assert.Empty(Vacuum.Lint(WithProperty(property)));

    [Theory]
    [InlineData("format: int32, minimum: 0", "owasp-integer-limit")]
    [InlineData("format: int32, maximum: 9", "owasp-integer-limit")]
    [InlineData("minimum: 0, maximum: 9", "owasp-integer-format")]
    public void Lint_UnboundedInteger_Reports(string keywords, string expected) =>
        Assert.Equal([expected], Vacuum.Lint(WithProperty($"count: {{ type: integer, {keywords} }}")));

    [Fact]
    public void Lint_IntegerWithExclusiveBounds_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(
            WithProperty("count: { type: integer, format: int32, exclusiveMinimum: 0, exclusiveMaximum: 10 }")));

    [Fact]
    public void Lint_ConditionalWrite_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(WithResource(Resource)));

    [Theory]
    [InlineData("delete:")]
    [InlineData("put:")]
    [InlineData("patch:")]
    public void Lint_WriteWithoutIfMatch_Reports(string method) =>
        Assert.Equal(["dt-conditional-write"], Vacuum.Lint(WithResource(Edit(
            Edit(Resource, "    delete:", "    " + method),
            "        - { name: If-Match, in: header, description: The ETag from the caller's last read., required: true, example: '\"3\"', schema: { type: string, maxLength: 64, pattern: '^\"[^\"]*\"$' } }\n",
            ""))));

    [Fact]
    public void Lint_WriteWithout412_Reports() =>
        Assert.Equal(["dt-conditional-write"], Vacuum.Lint(WithResource(WithoutLine(Resource, "'412':"))));

    [Fact]
    public void Lint_WriteWithout428_Reports() =>
        Assert.Equal(["dt-conditional-write"], Vacuum.Lint(WithResource(WithoutLine(Resource, "'428':"))));

    [Fact]
    public void Lint_OptionalIfMatch_Reports() =>
        Assert.Equal(["dt-conditional-write"], Vacuum.Lint(WithResource(Edit(
            Resource,
            "description: The ETag from the caller's last read., required: true,",
            "description: The ETag from the caller's last read., required: false,"))));

    [Fact]
    public void Lint_ReadWithoutETag_Reports() =>
        Assert.Equal(
            ["dt-etag-response"],
            Vacuum.Lint(WithResource(Edit(Resource, "headers: { ETag:", "headers: { Version:"))));

    // The read needs no ETag once every write on the resource opts out.
    [Fact]
    public void Lint_UnconditionalWriteWithReason_FindsNothing()
    {
        var resource = Edit(
            Resource,
            "operationId: DeleteProject\n",
            "operationId: DeleteProject\n      x-dt-unconditional-write: Only the owner writes it.\n");
        resource = WithoutLine(WithoutLine(resource, "'412':"), "'428':");

        Assert.Empty(Vacuum.Lint(WithResource(Edit(resource, "headers: { ETag:", "headers: { Version:"))));
    }

    [Fact]
    public void Lint_UnconditionalWriteWithoutReason_Reports() =>
        Assert.Equal(["dt-conditional-write"], Vacuum.Lint(WithResource(WithoutLine(
            Edit(
                Resource,
                "operationId: DeleteProject\n",
                "operationId: DeleteProject\n      x-dt-unconditional-write: ''\n"),
            "'412':"))));

    [Fact]
    public void Lint_IdempotentCreate_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(WithResource(Create)));

    [Fact]
    public void Lint_CreateWithoutIdempotencyKey_Reports() =>
        Assert.Equal(
            ["dt-idempotent-create"],
            Vacuum.Lint(WithResource(Edit(Create, "name: Idempotency-Key, in: header", "name: Request-Id, in: header"))));

    [Fact]
    public void Lint_CreateWithout422_Reports() =>
        Assert.Equal(["dt-idempotent-create"], Vacuum.Lint(WithResource(WithoutLine(Create, "'422':"))));

    [Fact]
    public void Lint_OptionalIdempotencyKey_Reports() =>
        Assert.Equal(["dt-idempotent-create"], Vacuum.Lint(
            WithResource(Edit(Create, "first response., required: true,", "first response., required: false,"))));

    [Fact]
    public void Lint_CreateWithout400_Reports() =>
        Assert.Equal(["dt-idempotent-create"], Vacuum.Lint(WithResource(WithoutLine(Create, "'400':"))));

    [Fact]
    public void Lint_CreateWithoutKeyWithReason_FindsNothing()
    {
        var create = Edit(
            Create,
            "operationId: CreateProject\n",
            "operationId: CreateProject\n      x-dt-no-idempotency-key: The title is unique, so a repeat gets 409.\n");

        Assert.Empty(
            Vacuum.Lint(WithResource(Edit(create, "name: Idempotency-Key, in: header", "name: Request-Id, in: header"))));
    }

    [Fact]
    public void Lint_CreateWithoutKeyOrReason_Reports() =>
        Assert.Equal(["dt-idempotent-create"], Vacuum.Lint(WithResource(Edit(
            Edit(
                Create,
                "operationId: CreateProject\n",
                "operationId: CreateProject\n      x-dt-no-idempotency-key: ''\n"),
            "name: Idempotency-Key, in: header",
            "name: Request-Id, in: header"))));

    [Fact]
    public void Lint_TextJsonRequestBody_Reports() =>
        Assert.Equal(["dt-request-media-type"], Vacuum.Lint(Edit(
            Clean,
            "content: { application/json: { schema: { $ref: '#/components/schemas/Archive'",
            "content: { text/json: { schema: { $ref: '#/components/schemas/Archive'")));

    // The request body is a $ref to Archive, so these also check that the rule follows it.
    [Theory]
    [InlineData("pageSize: { type: integer, format: int32, minimum: 1, maximum: 100 }")]
    [InlineData("pageCursor: { type: string, maxLength: 200, pattern: '^[a-zA-Z0-9_-]*$' }")]
    [InlineData("page: { type: integer, format: int32, minimum: 1, maximum: 1000 }")]
    [InlineData("skip: { type: integer, format: int32, minimum: 0, maximum: 10000 }")]
    [InlineData("top: { type: integer, format: int32, minimum: 1, maximum: 100 }")]
    public void Lint_PagingInRequestBody_Reports(string property) =>
        Assert.Equal(["dt-paging-in-query"], Vacuum.Lint(WithBodyProperty(property)));

    [Theory]
    [InlineData("pages: { type: integer, format: int32, minimum: 1, maximum: 1000 }")]
    [InlineData("topic: { type: string, maxLength: 20, pattern: '^[a-z]*$' }")]
    public void Lint_BodyFieldLikePaging_FindsNothing(string property) =>
        Assert.Empty(Vacuum.Lint(WithBodyProperty(property)));

    // A response field like nextPageCursor, or one named pageSize, is fine: the rule covers what a client sends.
    [Fact]
    public void Lint_PagingInResponse_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(WithProperty("pageSize: { type: integer, format: int32, minimum: 1, maximum: 100 }")));

    [Fact]
    public void LintBaseline_CleanDocument_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Clean, Baseline));

    // House style: the full ruleset reports each one, the baseline none.
    [Theory]
    [InlineData("/api/v1/projects/{projectId}:archive:", "/api/projects/{projectId}:archive:", "dt-path-version")]
    [InlineData("status: { $ref", "Plan_status: { type: string, maxLength: 20, pattern: '^[a-z]*$', description: Added by this test. }, status: { $ref", "dt-property-casing")]
    [InlineData(
        "status: { $ref",
        "created: { type: string, format: date-time, maxLength: 33, description: Added by this test. }, status: { $ref",
        "dt-timestamp-name")]
    [InlineData(", description: When the project was created in UTC. }", " }", "dt-description")]
    [InlineData("properties: { reason:", "properties: { pageSize: { type: integer, format: int32, minimum: 1, maximum: 100, description: Added by this test. }, reason:", "dt-paging-in-query")]
    [InlineData(", example: { title: Unauthorized, status: 401 }", "", "dt-error-example")]
    [InlineData("title: Unauthorized, status: 401", "title: Unauthorized, status: 404", "dt-error-example")]
    public void LintBaseline_HouseStyle_FindsNothing(string from, string to, string full)
    {
        var yaml = Edit(Clean, from, to);
        Assert.Equal([full], Vacuum.Lint(yaml));
        Assert.Empty(Vacuum.Lint(yaml, Baseline));
    }

    // The industry basics: the baseline reports each one too.
    [Theory]
    [InlineData(
        "reason: { type: string, maxLength: 500, ",
        "reason: { type: string, ",
        "owasp-string-limit",
        "dt-string-limit")]
    [InlineData("      parameters:\n", "      parameters:\n        - { name: email, in: query, description: Whose plan., example: a, schema: { type: string, maxLength: 20, pattern: '^[a-z]*$' } }\n", "dt-no-personal-data-in-url", "dt-no-personal-data-in-url")]
    [InlineData("      parameters:\n", "      parameters:\n        - { name: apiKey, in: query, description: The key., example: a, schema: { type: string, maxLength: 20, pattern: '^[a-z]*$' } }\n", "owasp-no-credentials-in-url", "owasp-no-credentials-in-url")]
    [InlineData("      summary: Archives a project.\n", "", "dt-operation-summary", "dt-operation-summary")]
    public void LintBaseline_IndustryBasics_Reports(string from, string to, string baseline, string full)
    {
        var yaml = Edit(Clean, from, to);
        Assert.Equal([baseline], Vacuum.Lint(yaml, Baseline));
        Assert.Equal([full], Vacuum.Lint(yaml));
    }

    [Fact]
    public void Lint_OpenRequestSchema_Reports() =>
        Assert.Equal(["owasp-no-additionalProperties"], Vacuum.Lint(
            Edit(Clean, "Archive: { type: object, additionalProperties: false,", "Archive: { type: object,")));

    [Fact]
    public void Lint_UnboundedRequestMap_Reports() =>
        Assert.Equal(
            ["owasp-constrained-additionalProperties"],
            Vacuum.Lint(Edit(
                Clean,
                "Archive: { type: object, additionalProperties: false,",
                "Archive: { type: object, additionalProperties: { type: boolean },")));

    [Fact]
    public void Lint_BoundedRequestMap_FindsNothing() =>
        Assert.Empty(Vacuum.Lint(Edit(
            Clean,
            "Archive: { type: object, additionalProperties: false,",
            "Archive: { type: object, maxProperties: 9, additionalProperties: { type: boolean },")));

    [Fact]
    public void Lint_NoSecurity_Reports() =>
        Assert.Equal(
            ["oas3-unused-component", "owasp-protection-global-unsafe", "owasp-protection-global-unsafe-strict"],
            Vacuum.Lint(Edit(Clean, "security: [{ bearer: [] }]\n", "")));

    [Fact]
    public void Lint_NumericId_Reports() =>
        Assert.Equal(["owasp-no-numeric-ids"], Vacuum.Lint(Edit(
            Clean,
            "example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: { type: string, format: uuid, maxLength: 36 } }\n",
            "example: 7, schema: { type: integer, format: int32, minimum: 1, maximum: 9 } }\n")));

    [Fact]
    public void Lint_ApiKeyInQuery_Reports()
    {
        var yaml = Edit(
            Edit(Clean, "security: [{ bearer: [] }]", "security: [{ key: [] }]"),
            "  securitySchemes:\n",
            "  securitySchemes:\n    key: { type: apiKey, in: query, name: apiKey, description: An API key sent in the query string. }\n");

        Assert.Equal(["oas3-unused-component", "owasp-no-api-keys-in-url"], Vacuum.Lint(yaml));
    }

    private static string Edit(string yaml, string find, string replacement)
    {
        Assert.Contains(find, yaml, StringComparison.Ordinal);
        return yaml.Replace(find, replacement, StringComparison.Ordinal);
    }

    /// <summary>Adds a GET on <paramref name="path"/>, declaring each of its path parameters.</summary>
    private static string WithPath(string path)
    {
        var names = path.Split('{').Skip(1).Select(part => part[..part.IndexOf('}', StringComparison.Ordinal)])
            .ToList();
        var parameters = names.Count == 0 ? "" : "      parameters:\n" + string.Concat(names.Select(name =>
            $"        - {{ name: {name}, in: path, description: Added by this test., required: true, example: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73, schema: {{ type: string, format: uuid, maxLength: 36 }} }}\n"));

        return Edit(Clean, "paths:\n", $"""
            paths:
              {path}:
                get:
                  tags: [Projects]
                  summary: Gets.
                  description: Returns the resource at this path.
                  operationId: GetExtra

            """ + parameters + $$"""
                  responses:
                    {{ProjectResponse}}
                    {{UnauthorizedResponse}}
                    '429': { $ref: '#/components/responses/TooManyRequests' }
                    '500': { $ref: '#/components/responses/Failed' }

            """);
    }

    // Removes the one line that starts with start, after its indentation.
    private static string WithoutLine(string yaml, string start)
    {
        var lines = yaml.Split('\n');
        Assert.Single(lines, line => line.TrimStart().StartsWith(start, StringComparison.Ordinal));
        return string.Join('\n', lines.Where(line => !line.TrimStart().StartsWith(start, StringComparison.Ordinal)));
    }

    private static string WithResource(string resource) =>
        Edit(Clean, "paths:\n", "paths:\n" + resource);

    private static string WithParameter(string parameter) =>
        Edit(Clean, "      parameters:\n", $"      parameters:\n        - {parameter}\n");

    private static string WithQuery(string name) =>
        WithParameter(
            $"{{ name: {name}, in: query, description: Added by this test., example: abc, schema: {BoundedString} }}");

    // Adds a property to Project, with a description so only the rule under test fires.
    private static string WithProperty(string property) =>
        Edit(
            Clean,
            "status: { $ref",
            $"{property[..property.LastIndexOf('}')]}, description: Added by this test. }}, status: {{ $ref");

    // Adds a property to Archive, the request body.
    private static string WithBodyProperty(string property) =>
        Edit(
            Clean,
            "properties: { reason:",
            $"properties: {{ {property[..property.LastIndexOf('}')]}, description: Added by this test. }}, reason:");
}
