namespace Digitals.AsyncApi.Tests;

/// <summary>
/// Each test edits a document that passes every rule and checks exactly which rules then fire.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RulesetTests
{
    // One event that follows every rule, in the YAML a designer writes by hand.
    internal const string Clean = """
        asyncapi: 3.1.0
        info:
          title: Notes events
          version: 1.0.0
          description: Events the notes service publishes when a note changes.
          tags:
            - name: Notes
              description: Notes, each a single line of text with the time it was created.
        channels:
          noteCreated:
            address: notes.v1.created
            title: Note created
            description: Every note the service creates, once each.
            messages:
              noteCreated:
                $ref: '#/components/messages/NoteCreated'
        operations:
          PublishNoteCreated:
            action: send
            channel:
              $ref: '#/channels/noteCreated'
            summary: Publishes each new note.
            description: Sends a NoteCreated message once the note is stored.
            messages:
              - $ref: '#/channels/noteCreated/messages/noteCreated'
        components:
          messages:
            NoteCreated:
              name: NoteCreated
              title: Note created
              summary: A note was created.
              description: The note as stored, sent once after it is created.
              contentType: application/json
              payload:
                $ref: '#/components/schemas/Note'
              examples:
                - name: officeSupplies
                  summary: A note about office supplies.
                  payload:
                    noteId: 3f2a9c1e-7b4d-4e0a-8c5f-6d1b2a9e0c73
                    title: Order more paper for the office printer.
                    createTime: '2026-10-29T09:30:00Z'
                    status: active
          schemas:
            Note:
              type: object
              description: A note as the service stores it.
              required: [noteId, title, createTime, status]
              properties:
                noteId: { type: string, format: uuid, maxLength: 36, description: Assigned by the service on create. }
                title: { type: string, maxLength: 200, description: The note's text, on one line. }
                createTime: { type: string, format: date-time, maxLength: 33, description: When the service created the note, in UTC. }
                status: { type: string, enum: [active, archived], description: Where the note is in its lifecycle. }

        """;

    [Fact]
    public void Lint_ConventionalDocument_FindsNothing() =>
        Assert.Empty(Spectral.Lint(Clean));

    [Fact]
    public void LintBaseline_ConventionalDocument_FindsNothing() =>
        Assert.Empty(Spectral.Lint(Clean, baseline: true));

    // examples/asyncapi.yaml uses every part of AsyncAPI 3 the viewer shows: bindings, traits, replies and the rest.
    [Fact]
    public void Lint_FullExample_FindsNothing() =>
        Assert.Empty(
            Spectral.Lint(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "asyncapi.yaml"))));

    [Fact]
    public void Lint_KafkaBindingEnum_KeepsTheBrokersNaming() =>
        Assert.Empty(Spectral.Lint(Edit(
            Clean,
            "    action: send\n",
            "    action: send\n    bindings:\n      kafka:\n        clientId: { type: string, enum: [notes-service] }\n        bindingVersion: '0.5.0'\n")));

    // The industry basics: both levels report each one.
    [Theory]
    [InlineData(
        "    title: Order more paper for the office printer.",
        "    title: 7",
        "dt-message-example-valid")]
    [InlineData("    status: active", "    status: deleted", "dt-message-example-valid")]
    [InlineData(
        "    address: notes.v1.created\n",
        "    address: notes.v1.{email}.created\n    parameters:\n      email:\n        description: Whose notes.\n",
        "dt-no-personal-data-in-channel")]
    public void LintBaseline_IndustryBasics_Reports(string find, string replacement, string expected)
    {
        var yaml = Edit(Clean, find, replacement);
        Assert.Equal([expected], Spectral.Lint(yaml, baseline: true));
        Assert.Equal([expected], Spectral.Lint(yaml));
    }

    // House rules: the full ruleset reports each one, the baseline none.
    [Theory]
    [InlineData(", description: The note's text, on one line. }", " }", "dt-description")]
    [InlineData("    description: Every note the service creates, once each.\n", "", "dt-description")]
    [InlineData("      examples:\n", "      x-examples:\n", "dt-message-example")]
    [InlineData("noteId: { type: string", "note_id: { type: string", "dt-property-casing")]
    [InlineData("enum: [active, archived]", "enum: [active, ON_HOLD]", "dt-enum-casing")]
    [InlineData("createTime: { type: string", "created: { type: string", "dt-timestamp-name")]
    [InlineData("    title: { type: string, maxLength: 200,", "    title: { type: string,", "dt-string-limit")]
    [InlineData(
        "    status: { type: string",
        "    tags: { type: array, items: { type: string, maxLength: 20 }, description: Labels the author chose. }\n        status: { type: string",
        "dt-array-limit")]
    public void LintBaseline_HouseRule_FindsNothing(string find, string replacement, string expected)
    {
        var yaml = Edit(Clean, find, replacement);
        Assert.Equal([expected], Spectral.Lint(yaml));
        Assert.Empty(Spectral.Lint(yaml, baseline: true));
    }

    [Fact]
    public void Lint_DateNamedLikeATimestamp_Reports() =>
        Assert.Equal(["dt-date-name"], Spectral.Lint(Edit(
            Clean,
            "    status: { type: string",
            "    dueTime: { type: string, format: date, maxLength: 10, description: When it's due. }\n        status: { type: string")));

    private static string Edit(string yaml, string find, string replacement)
    {
        Assert.Contains(find, yaml, StringComparison.Ordinal);
        return yaml.Replace(find, replacement, StringComparison.Ordinal);
    }
}
