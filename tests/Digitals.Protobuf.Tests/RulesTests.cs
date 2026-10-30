namespace Digitals.Protobuf.Tests;

/// <summary>
/// Each test edits a file that passes every rule and checks exactly which rules then fire.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RulesTests
{
    // One service that follows every rule, as a designer writes it by hand.
    internal const string Clean = """
        syntax = "proto3";

        package acme.notes.v1;

        import "google/protobuf/timestamp.proto";

        // Keeps short single-line notes.
        service NoteService {
          // Reads one note by the id the service assigned when it was created.
          rpc GetNote(GetNoteRequest) returns (GetNoteResponse);
        }

        // Asks for one note.
        message GetNoteRequest {
          // The note's id.
          string note_id = 1;
        }

        // The note asked for.
        message GetNoteResponse {
          // The note.
          Note note = 1;
        }

        // A note as the service stores it.
        message Note {
          // Assigned by the service on create.
          string note_id = 1;
          // The note's text, on one line.
          string title = 2;
          // When the service created the note.
          google.protobuf.Timestamp create_time = 3;
          // Where the note is in its lifecycle.
          NoteState state = 4;
        }

        // Where a note is in its lifecycle.
        enum NoteState {
          // Not set. The service never sends it.
          NOTE_STATE_UNSPECIFIED = 0;
          // Shown in lists.
          NOTE_STATE_ACTIVE = 1;
        }

        """;

    [Fact]
    public void Lint_ConventionalFile_FindsNothing() =>
        Assert.Empty(Buf.Lint(Clean));

    // examples/protobuf uses every part of a contract the docs page shows: streams, oneofs, maps, nested types.
    [Fact]
    public void Lint_FullExample_FindsNothing()
    {
        var example = Path.Combine(AppContext.BaseDirectory, "examples", "protobuf");

        Assert.Equal(0, Buf.Tool("lint", example).ExitCode);
        Assert.Equal(0, Buf.Tool("format", example).ExitCode);
    }

    [Fact]
    public void LintBaseline_ConventionalFile_FindsNothing() =>
        Assert.Empty(Buf.Lint(Clean, baseline: true));

    // The style guide's basics: both levels report each one.
    [Theory]
    [InlineData("  string title = 2;", "  string noteTitle = 2;", "FIELD_LOWER_SNAKE_CASE")]
    [InlineData(
        "// Where a note is in its lifecycle.\n",
        "// A label.\nmessage note_label {\n  // Its text.\n  string text = 1;\n}\n\n// Where a note is in its lifecycle.\n",
        "MESSAGE_PASCAL_CASE")]
    [InlineData("  NOTE_STATE_ACTIVE = 1;", "  NOTE_STATE_Active = 1;", "ENUM_VALUE_UPPER_SNAKE_CASE")]
    [InlineData(
        "import \"google/protobuf/timestamp.proto\";\n",
        "import \"google/protobuf/timestamp.proto\";\nimport \"google/protobuf/duration.proto\";\n",
        "IMPORT_USED")]
    public void LintBaseline_StyleGuide_Reports(string find, string replacement, string expected)
    {
        var proto = Edit(Clean, find, replacement);
        Assert.Equal([expected], Buf.Lint(proto));
        Assert.Equal([expected], Buf.Lint(proto, baseline: true));
    }

    [Fact]
    public void LintBaseline_PackageOutsideItsDirectory_Reports() =>
        Assert.Equal(["PACKAGE_DIRECTORY_MATCH"], Buf.Lint(Clean, baseline: true, directory: "notes"));

    // House rules: the full config reports each one, the baseline none.
    [Theory]
    [InlineData("  // The note's text, on one line.\n", "", "COMMENT_FIELD")]
    [InlineData("// A note as the service stores it.\n", "", "COMMENT_MESSAGE")]
    [InlineData("  // Reads one note by the id the service assigned when it was created.\n", "", "COMMENT_RPC")]
    [InlineData("// Keeps short single-line notes.\n", "", "COMMENT_SERVICE")]
    [InlineData("  // Shown in lists.\n", "", "COMMENT_ENUM_VALUE")]
    [InlineData("service NoteService {", "service Notes {", "SERVICE_SUFFIX")]
    [InlineData("  NOTE_STATE_ACTIVE = 1;", "  ACTIVE = 1;", "ENUM_VALUE_PREFIX")]
    [InlineData("  NOTE_STATE_UNSPECIFIED = 0;", "  NOTE_STATE_UNKNOWN = 0;", "ENUM_ZERO_VALUE_SUFFIX")]
    public void LintBaseline_HouseRule_FindsNothing(string find, string replacement, string expected)
    {
        var proto = Edit(Clean, find, replacement);
        Assert.Equal([expected], Buf.Lint(proto));
        Assert.Empty(Buf.Lint(proto, baseline: true));
    }

    [Fact]
    public void Lint_PackageWithoutVersion_Reports() =>
        Assert.Equal(
            ["PACKAGE_VERSION_SUFFIX"],
            Buf.Lint(Edit(Clean, "package acme.notes.v1;", "package acme.notes;"), directory: "acme/notes"));

    [Fact]
    public void Lint_SharedRequestMessage_Reports()
    {
        var proto = Edit(
            Clean,
            "  rpc GetNote(GetNoteRequest) returns (GetNoteResponse);\n",
            "  rpc GetNote(GetNoteRequest) returns (GetNoteResponse);\n  // Reads a note's newest revision.\n  rpc GetLatestNote(GetNoteRequest) returns (GetNoteResponse);\n");

        Assert.Contains("RPC_REQUEST_RESPONSE_UNIQUE", Buf.Lint(proto));
    }

    [Fact]
    public void Breaking_NewField_FindsNothing() =>
        Assert.Empty(Buf.Breaking(Clean, Edit(
            Clean,
            "  NoteState state = 4;\n",
            "  NoteState state = 4;\n  // Added by this test.\n  string owner_id = 5;\n")));

    [Theory]
    [InlineData("  // The note's text, on one line.\n  string title = 2;\n", "", "FIELD_NO_DELETE")]
    [InlineData("  string title = 2;", "  string text = 2;", "FIELD_SAME_NAME")]
    [InlineData("  string title = 2;", "  int64 title = 2;", "FIELD_SAME_TYPE")]
    [InlineData("  // Shown in lists.\n  NOTE_STATE_ACTIVE = 1;\n", "", "ENUM_VALUE_NO_DELETE")]
    public void Breaking_BreakingChange_Reports(string find, string replacement, string expected) =>
        Assert.Contains(expected, Buf.Breaking(Clean, Edit(Clean, find, replacement)));

    private static string Edit(string proto, string find, string replacement)
    {
        Assert.Contains(find, proto, StringComparison.Ordinal);
        return proto.Replace(find, replacement, StringComparison.Ordinal);
    }
}
