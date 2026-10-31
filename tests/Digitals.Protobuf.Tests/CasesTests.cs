using System.Text.Json;

namespace Digitals.Protobuf.Tests;

/// <summary>`dt-protobuf cases`: the test cases a contract implies, end to end through the tool.</summary>
[Trait("Category", "Integration")]
public sealed class CasesTests
{
    // Every kind of RPC the cases name and every protovalidate rule they cover, on one contract. The streaming
    // RPCs take a Note, so its rules are the request's own; the unary ones hold it in a field. Note holds itself,
    // a map of messages, a list of messages, and a rule on a type it doesn't fit (misuse). rules.proto adds a
    // predefined rule to each kind that takes one, which the cases name but can't explain.
    private const string Notes = """
        syntax = "proto3";

        package acme.notes.v1;

        import "buf/validate/validate.proto";
        import "google/protobuf/any.proto";
        import "google/protobuf/descriptor.proto";
        import "google/protobuf/duration.proto";
        import "google/protobuf/empty.proto";
        import "google/protobuf/field_mask.proto";
        import "google/protobuf/timestamp.proto";
        import "acme/notes/v1/rules.proto";

        extend google.protobuf.OneofOptions {
          optional bool tagged = 50001;
        }

        // Keeps notes.
        service NoteService {
          // Reads a note.
          // Fails with NOT_FOUND when no note has the ID, and with PERMISSION_DENIED. NOT_FOUND again; DATA_LOSS too.
          rpc GetNote(GetNoteRequest) returns (Note) {
            option idempotency_level = NO_SIDE_EFFECTS;
          }
          rpc CreateNote(CreateNoteRequest) returns (google.protobuf.Empty);
          rpc UpdateNote(UpdateNoteRequest) returns (Note);
          rpc UploadNotes(stream Note) returns (UploadNotesResponse);
          rpc WatchNotes(google.protobuf.Empty) returns (stream Note);
          rpc SyncNotes(stream Note) returns (stream Note) {
            option deprecated = true;
          }
        }

        message GetNoteRequest {
          string name = 1;
          Note note = 2;
        }

        message CreateNoteRequest {
          string request_id = 1;
          Note note = 2;
        }

        message UpdateNoteRequest {
          Note note = 1;
          google.protobuf.FieldMask update_mask = 2;
        }

        message UploadNotesResponse {
          int32 count = 1;
        }

        message Attachment {
          string name = 1 [(buf.validate.field).string.max_len = 40];
        }

        message Old {
          option deprecated = true;
          string x = 1;
        }

        message Note {
          option (buf.validate.message).cel = {id: "dates", message: "ends after it starts", expression: "true"};
          option (buf.validate.message).cel = {expression: "has(this.title)"};
          option (buf.validate.message).cel_expression = "true";
          option (buf.validate.message).cel_expression = "false";
          option (buf.validate.message).oneof = {fields: ["title", "body"], required: true};
          option (buf.validate.message).oneof = {fields: ["summary"]};

          enum Kind {
            KIND_UNSPECIFIED = 0;
            KIND_TEXT = 1;
            KIND_LIST = 2;
          }

          string title = 1 [(buf.validate.field).required = true, (buf.validate.field).string = {min_len: 1, max_len: 80, pattern: "^[A-Z]", prefix: "T", suffix: "s", contains: "e", not_contains: "!", example: "Tests", uuid: false}];
          string body = 2 [(buf.validate.field).string = {len: 5, min_bytes: 3, max_bytes: 9, len_bytes: 6, const: "Hello"}];
          string summary = 3 [(buf.validate.field).string = {min_len: 3, in: ["a", "b"], not_in: ["c"], well_known_regex: KNOWN_REGEX_HTTP_HEADER_NAME, strict: false, [acme.notes.v1.shouting]: true}];
          string code = 4 [(buf.validate.field).string.pattern = ".*"];
          string tag = 5 [(buf.validate.field).string.pattern = "(?P<x>a)"];
          string email = 6 [(buf.validate.field).string.email = true];
          bytes digest = 7 [(buf.validate.field).bytes = {len: 4, min_len: 1, max_len: 8, const: "\x01\x02", prefix: "\x01", suffix: "\x02", contains: "\x03", in: ["\x01"], not_in: ["\x02"], pattern: "^a"}];
          bytes address = 8 [(buf.validate.field).bytes.ipv4 = true];
          int32 size = 9 [(buf.validate.field).int32 = {gte: 1, lte: 100, [acme.notes.v1.even]: true}];
          int32 rank = 10 [(buf.validate.field).int32 = {gt: 0, lt: 10, const: 5, in: [1, 2, 3], not_in: [4]}];
          int64 total = 11 [(buf.validate.field).int64.gte = 5];
          uint64 big = 12 [(buf.validate.field).uint64 = {in: [1, 2, 3, 4, 5, 6]}];
          float ratio = 13 [(buf.validate.field).float = {lte: 1.5, gt: 0, finite: true}];
          float limit = 14 [(buf.validate.field).float.lte = inf];
          double weight = 15 [(buf.validate.field).double = {in: [0.5, inf], not_in: [0.25]}];
          bool pinned = 16 [(buf.validate.field).bool = {const: true, [acme.notes.v1.set]: true}];
          Kind kind = 17 [(buf.validate.field).enum = {defined_only: true, in: [1, 2], not_in: [0], const: 1, [acme.notes.v1.known]: true}];
          repeated Kind kinds = 18 [(buf.validate.field).repeated = {min_items: 1, max_items: 3, unique: true, items: {enum: {in: [0, 1, 2]}}, [acme.notes.v1.sorted]: true}];
          int32 size_code = 19 [(buf.validate.field).enum.defined_only = true];
          map<string, int32> labels = 20 [(buf.validate.field).map = {min_pairs: 1, max_pairs: 3, keys: {string: {min_len: 2}}, values: {int32: {gte: 0}}, [acme.notes.v1.small]: true}];
          map<string, Attachment> children = 21;
          Note parent = 22;
          repeated Attachment attachments = 23 [(buf.validate.field).repeated.min_items = 3];
          google.protobuf.Timestamp at = 24 [(buf.validate.field).timestamp = {gt: {seconds: 1767225600}, lt_now: true, within: {seconds: 60}}];
          google.protobuf.Timestamp started = 25 [(buf.validate.field).timestamp = {gt_now: true, lte: {seconds: 1767225600, nanos: 500000000}}];
          google.protobuf.Duration length = 26 [(buf.validate.field).duration = {gte: {seconds: 900}, lte: {seconds: 1, nanos: 500000000}, in: [{seconds: 60}], not_in: [{seconds: 30}]}];
          google.protobuf.Any payload = 27 [(buf.validate.field).any = {in: ["type.googleapis.com/acme.A"], not_in: ["type.googleapis.com/acme.B"]}];
          google.protobuf.FieldMask mask = 28 [(buf.validate.field).field_mask = {const: {paths: ["title"]}, in: ["title", "body"], not_in: ["id"], [acme.notes.v1.narrow]: true}];
          string nickname = 29 [(buf.validate.field).required = false, (buf.validate.field).ignore = IGNORE_ALWAYS];
          string rule = 30 [(buf.validate.field).cel = {id: "upper", message: "must be upper case", expression: "this == this.upperAscii()"}, (buf.validate.field).cel = {expression: "this != ''"}, (buf.validate.field).cel_expression = "size(this) < 9", (buf.validate.field).cel_expression = "true"];
          string misuse = 31 [(buf.validate.field).map.min_pairs = 1];
          string legacy = 32 [deprecated = true];
          Old old = 33;
          oneof attendance {
            option (buf.validate.oneof).required = true;
            string room = 34;
            string link = 35;
          }
          oneof style {
            option (tagged) = true;
            string color = 36;
          }
          optional string mood = 37;
        }
        """;

    private const string Rules = """
        syntax = "proto2";

        package acme.notes.v1;

        import "buf/validate/validate.proto";

        extend buf.validate.StringRules {
          optional bool shouting = 1161;
        }

        extend buf.validate.Int32Rules {
          optional bool even = 1161;
        }

        extend buf.validate.EnumRules {
          optional bool known = 1161;
        }

        extend buf.validate.FieldMaskRules {
          optional bool narrow = 1161;
        }

        extend buf.validate.BoolRules {
          optional bool set = 1161;
        }

        extend buf.validate.RepeatedRules {
          optional bool sorted = 1161;
        }

        extend buf.validate.MapRules {
          optional bool small = 1161;
        }
        """;

    // A file with no package, whose RPC takes and returns Empty.
    private const string Ping = """
        syntax = "proto3";

        import "google/protobuf/empty.proto";

        service PingService {
          rpc Ping(google.protobuf.Empty) returns (google.protobuf.Empty);
        }
        """;

    private static readonly Lazy<string> Module = new(() => Write(
        ("acme/notes/v1/notes.proto", Notes),
        ("acme/notes/v1/rules.proto", Rules),
        ("ping.proto", Ping)));

    private static readonly Lazy<(Dictionary<string, JsonElement> Cases, string Output)> Listed = new(() =>
    {
        var (_, output) = Buf.Tool("cases", Module.Value, "--format", "json");
        using var json = JsonDocument.Parse(output[output.IndexOf('[', StringComparison.Ordinal)..(output.LastIndexOf(']') + 1)]);
        var cases = json.RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.Clone(), StringComparer.Ordinal);
        return (cases, output);
    });

    [Theory]
    [InlineData("NoteService.GetNote.OK", "Send a valid GetNoteRequest. Expect OK with a Note.")]
    [InlineData("NoteService.GetNote.NOT_FOUND", "Call it when no note has the ID. Expect NOT_FOUND.")]
    [InlineData("NoteService.GetNote.PERMISSION_DENIED", "Call with credentials that lack the permission. Expect PERMISSION_DENIED.")]
    [InlineData("NoteService.GetNote.DATA_LOSS", "Bring about this status. Expect DATA_LOSS.")]
    [InlineData("NoteService.CreateNote.OK", "Send a valid CreateNoteRequest. Expect OK.")]
    [InlineData("NoteService.CreateNote.retry", "Send the same request twice with the same request_id. Expect the first result both times and the change made once.")]
    [InlineData("NoteService.UpdateNote.update_mask.unknown_path", "Send an update_mask naming a field that doesn't exist. Expect INVALID_ARGUMENT.")]
    [InlineData("NoteService.UploadNotes.OK", "Send a stream of valid Note messages and close it. Expect OK with an UploadNotesResponse.")]
    [InlineData("NoteService.WatchNotes.OK", "Send a valid Empty. Expect a stream of Note messages, then OK.")]
    [InlineData("NoteService.SyncNotes.OK", "Send a stream of valid Note messages and close it. Expect a stream of Note messages, then OK.")]
    [InlineData("PingService.Ping.OK", "Send a valid Empty. Expect OK.")]
    public void Cases_Call_SaysWhatToSendAndExpect(string id, string detail) =>
        Assert.Equal(detail, Listed.Value.Cases[id].GetProperty("detail").GetString());

    [Theory]
    [InlineData("dates", "Rejects a request that breaks dates (\"ends after it starts\")")]
    [InlineData("cel2", "Rejects a request that breaks cel2 (\"has(this.title)\")")]
    [InlineData("cel_expression", "Rejects a request that breaks the rule \"true\"")]
    [InlineData("cel_expression2", "Rejects a request that breaks the rule \"false\"")]
    [InlineData("oneof", "Rejects a request that sets none, or more than one, of title and body")]
    [InlineData("oneof2", "Rejects a request that sets more than one of summary")]
    [InlineData("attendance.required", "Rejects a request that sets none of room or link")]
    [InlineData("title.required", "Rejects a request without title")]
    [InlineData("title.min_len", "Accepts 1 character, rejects an empty value")]
    [InlineData("title.max_len", "Accepts 80 characters, rejects 81")]
    [InlineData("title.pattern", "Accepts a value matching ^[A-Z], rejects \"!\"")]
    [InlineData("title.prefix", "Rejects a value that doesn't start with \"T\"")]
    [InlineData("title.suffix", "Rejects a value that doesn't end with \"s\"")]
    [InlineData("title.contains", "Rejects a value without \"e\"")]
    [InlineData("title.not_contains", "Rejects a value containing \"!\"")]
    [InlineData("body.const", "Accepts \"Hello\", rejects \"not-Hello\"")]
    [InlineData("body.len", "Accepts 5 characters, rejects 6")]
    [InlineData("body.len_bytes", "Accepts 6 bytes, rejects 7")]
    [InlineData("body.min_bytes", "Accepts 3 bytes, rejects 2")]
    [InlineData("body.max_bytes", "Accepts 9 bytes, rejects 10")]
    [InlineData("summary.min_len", "Accepts 3 characters, rejects 2")]
    [InlineData("summary.in", "Accepts \"a\" and \"b\", rejects \"not-a\"")]
    [InlineData("summary.not_in", "Rejects \"c\"")]
    [InlineData("summary.well_known_regex", "Rejects a value that isn't a valid HTTP header name")]
    [InlineData("summary.shouting", "Rejects a value that breaks its shouting rule")]
    [InlineData("code.pattern", "Accepts a value matching .*, rejects a value that breaks it")]
    [InlineData("tag.pattern", "Accepts a value matching (?P<x>a), rejects a value that breaks it")]
    [InlineData("email.email", "Rejects \"no-at-sign\"")]
    [InlineData("digest.const", "Accepts 0x0102, rejects 0x010200")]
    [InlineData("digest.len", "Accepts 4 bytes, rejects 5")]
    [InlineData("digest.min_len", "Accepts 1 byte, rejects an empty value")]
    [InlineData("digest.max_len", "Accepts 8 bytes, rejects 9")]
    [InlineData("digest.pattern", "Accepts a value matching ^a, rejects \"!\"")]
    [InlineData("digest.prefix", "Rejects a value that doesn't start with 0x01")]
    [InlineData("digest.in", "Accepts 0x01, rejects 0x0100")]
    [InlineData("digest.not_in", "Rejects 0x02")]
    [InlineData("address.ipv4", "Rejects 16 bytes")]
    [InlineData("size.gte", "Accepts 1, rejects 0")]
    [InlineData("size.lte", "Accepts 100, rejects 101")]
    [InlineData("size.even", "Rejects a value that breaks its even rule")]
    [InlineData("rank.const", "Accepts 5, rejects 6")]
    [InlineData("rank.lt", "Accepts 9, rejects 10")]
    [InlineData("rank.gt", "Accepts 1, rejects 0")]
    [InlineData("rank.in", "Accepts 1, 2 and 3, rejects 4")]
    [InlineData("rank.not_in", "Rejects 4")]
    [InlineData("total.gte", "Accepts 5, rejects 4")]
    [InlineData("big.in", "Accepts each of its 6 values, rejects 7")]
    [InlineData("ratio.lte", "Accepts 1.5, rejects 1.51")]
    [InlineData("ratio.gt", "Accepts 0.01, rejects 0")]
    [InlineData("ratio.finite", "Rejects infinity and NaN")]
    [InlineData("limit.lte", "Rejects a value that breaks its lte rule")]
    [InlineData("weight.in", "Rejects a value that breaks its in rule")]
    [InlineData("weight.not_in", "Rejects 0.25")]
    [InlineData("pinned.const", "Accepts true, rejects false")]
    [InlineData("pinned.set", "Rejects a value that breaks its set rule")]
    [InlineData("kind.const", "Accepts KIND_TEXT, rejects any other value")]
    [InlineData("kind.defined_only", "Rejects the undefined number 3")]
    [InlineData("kind.in", "Accepts KIND_TEXT and KIND_LIST, rejects KIND_UNSPECIFIED")]
    [InlineData("kind.not_in", "Rejects KIND_UNSPECIFIED")]
    [InlineData("kind.known", "Rejects a value that breaks its known rule")]
    [InlineData("kinds.min_items", "Accepts 1 item, rejects an empty list")]
    [InlineData("kinds.max_items", "Accepts 3 items, rejects 4")]
    [InlineData("kinds.unique", "Rejects two equal items")]
    [InlineData("kinds[].in", "Accepts KIND_UNSPECIFIED, KIND_TEXT and KIND_LIST, rejects 3")]
    [InlineData("kinds.sorted", "Rejects a value that breaks its sorted rule")]
    [InlineData("size_code.defined_only", "Rejects the undefined number 1")]
    [InlineData("labels.min_pairs", "Accepts 1 pair, rejects an empty map")]
    [InlineData("labels.max_pairs", "Accepts 3 pairs, rejects 4")]
    [InlineData("labels.keys.min_len", "Accepts 2 characters, rejects 1")]
    [InlineData("labels.values.gte", "Accepts 0, rejects -1")]
    [InlineData("labels.small", "Rejects a value that breaks its small rule")]
    [InlineData("children.values.name.max_len", "Accepts 40 characters, rejects 41")]
    [InlineData("attachments.min_items", "Accepts 3 items, rejects 2")]
    [InlineData("attachments[].name.max_len", "Accepts 40 characters, rejects 41")]
    [InlineData("at.gt", "Accepts 2026-01-01T00:00:01Z, rejects 2026-01-01T00:00:00Z")]
    [InlineData("at.lt_now", "Rejects a time in the future")]
    [InlineData("at.within", "Rejects a time more than 60s from now")]
    [InlineData("started.lte", "Accepts 2026-01-01T00:00:00.5Z, rejects 2026-01-01T00:00:00.51Z")]
    [InlineData("started.gt_now", "Rejects a time in the past")]
    [InlineData("length.gte", "Accepts 900s, rejects 899s")]
    [InlineData("length.lte", "Accepts 1.5s, rejects 1.51s")]
    [InlineData("length.in", "Accepts 60s, rejects 61s")]
    [InlineData("length.not_in", "Rejects 30s")]
    [InlineData("payload.in", "Rejects a type other than \"type.googleapis.com/acme.A\"")]
    [InlineData("payload.not_in", "Rejects \"type.googleapis.com/acme.B\"")]
    [InlineData("mask.const", "Rejects a path list other than \"title\"")]
    [InlineData("mask.in", "Rejects a path other than \"title\" and \"body\"")]
    [InlineData("mask.not_in", "Rejects \"id\"")]
    [InlineData("mask.narrow", "Rejects a value that breaks its narrow rule")]
    [InlineData("rule.upper", "Rejects a request whose rule breaks upper (\"must be upper case\")")]
    [InlineData("rule.cel2", "Rejects a request whose rule breaks cel2 (\"this != ''\")")]
    [InlineData("rule.cel_expression", "Rejects a request whose rule breaks the rule \"size(this) < 9\"")]
    [InlineData("misuse.map", "Rejects a value that breaks its map rule")]
    public void Cases_Rule_SaysWhatIsAcceptedAndRejected(string id, string detail) =>
        Assert.Equal(
            $"{detail} with INVALID_ARGUMENT.",
            Listed.Value.Cases[$"NoteService.UploadNotes.{id}"].GetProperty("detail").GetString());

    // A rule on a field inside the request is named by the field's path.
    [Theory]
    [InlineData("NoteService.GetNote.note.dates", "Rejects a request whose note breaks dates (\"ends after it starts\") with INVALID_ARGUMENT.")]
    [InlineData("NoteService.GetNote.note.oneof", "Rejects a request that sets none, or more than one, of note.title and note.body with INVALID_ARGUMENT.")]
    [InlineData("NoteService.GetNote.note.attendance.required", "Rejects a request that sets none of note.room or note.link with INVALID_ARGUMENT.")]
    [InlineData("NoteService.GetNote.note.title.required", "Rejects a request without note.title with INVALID_ARGUMENT.")]
    [InlineData("NoteService.GetNote.note.attachments[].name.max_len", "Accepts 40 characters, rejects 41 with INVALID_ARGUMENT.")]
    public void Cases_NestedRule_IsNamedByItsPath(string id, string detail) =>
        Assert.Equal(detail, Listed.Value.Cases[id].GetProperty("detail").GetString());

    // An example and a false flag check nothing, ignore and required = false ask for nothing, a status the comment
    // names twice is one case, a message that holds itself is walked once, and a request without a request_id or
    // a field mask has neither case.
    [Theory]
    [InlineData("NoteService.UploadNotes.title.example")]
    [InlineData("NoteService.UploadNotes.title.uuid")]
    [InlineData("NoteService.UploadNotes.summary.strict")]
    [InlineData("NoteService.UploadNotes.nickname.required")]
    [InlineData("NoteService.UploadNotes.nickname.ignore")]
    [InlineData("NoteService.UploadNotes.parent.title.required")]
    [InlineData("NoteService.GetNote.retry")]
    [InlineData("NoteService.UploadNotes.retry")]
    public void Cases_RuleThatCantFail_IsNotListed(string id) =>
        Assert.DoesNotContain(id, Listed.Value.Cases.Keys);

    [Fact]
    public void Cases_StatusNamedTwice_IsOneCase() =>
        Assert.Single(Listed.Value.Cases.Keys, id => id == "NoteService.GetNote.NOT_FOUND");

    [Fact]
    public void Cases_Json_GivesEachCaseItsOperationKindAndPath()
    {
        var found = Listed.Value.Cases["NoteService.SyncNotes.OK"];

        Assert.Equal(
            ("NoteService.SyncNotes", "bidirectional streaming", "/acme.notes.v1.NoteService/SyncNotes", true),
            (found.GetProperty("operation").GetString(), found.GetProperty("kind").GetString(),
                found.GetProperty("path").GetString(), found.GetProperty("deprecated").GetBoolean()));
    }

    [Fact]
    public void Cases_Text_ListsEachRpcUnderItsKindAndPath()
    {
        var (exitCode, output) = Buf.Tool("cases", Module.Value);
        var text = output.ReplaceLineEndings("\n");

        Assert.Equal(0, exitCode);
        Assert.Contains("PingService.Ping  unary /PingService/Ping\n  PingService.Ping.OK  Send a valid Empty.", text, StringComparison.Ordinal);
        Assert.Contains("NoteService.SyncNotes  bidirectional streaming /acme.notes.v1.NoteService/SyncNotes (deprecated)\n", text, StringComparison.Ordinal);
        Assert.Contains($"{Listed.Value.Cases.Count} cases in 7 operations", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("calls", "NoteService.GetNote.NOT_FOUND", "NoteService.GetNote.note.title.required")]
    [InlineData("rules", "NoteService.GetNote.note.title.required", "NoteService.GetNote.NOT_FOUND")]
    public void Cases_Only_ListsOneKind(string only, string listed, string left)
    {
        var output = Buf.Tool("cases", Module.Value, "--only", only).Output;

        Assert.Contains($"  {listed} ", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"  {left} ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cases_ExampleContract_ListsEachRpcAndRule()
    {
        var (exitCode, output) = Buf.Tool("cases", Path.Combine(AppContext.BaseDirectory, "examples", "protobuf"));

        Assert.Equal(0, exitCode);
        Assert.Contains("  BookingService.UpdateBooking.update_mask.unknown_path ", output, StringComparison.Ordinal);
        Assert.Contains("  AttachmentService.AddAttachment.OK ", output, StringComparison.Ordinal);
        Assert.Contains(" Send a stream of valid AddAttachmentRequest messages and close it.", output, StringComparison.Ordinal);
        Assert.Contains("  BookingService.CreateBooking.duration.gte ", output, StringComparison.Ordinal);
        Assert.Contains(" Accepts 900s, rejects 899s with INVALID_ARGUMENT.", output, StringComparison.Ordinal);
    }

    // A contract that brings its own protovalidate keeps it, and a rule that copy has and the bundled one doesn't
    // is named all the same.
    [Fact]
    public void Cases_OwnProtovalidate_IsUsedInstead()
    {
        var module = Write(("buf/validate/validate.proto", OwnValidate), ("acme/asks/v1/asks.proto", Asks));

        var (exitCode, output) = Buf.Tool("cases", module, "--only", "rules");

        Assert.Equal(0, exitCode);
        Assert.Contains("AskService.Ask.sealed        Rejects a value that breaks its sealed rule with INVALID_ARGUMENT.", output, StringComparison.Ordinal);
        Assert.Contains("AskService.Ask.text.trimmed  Rejects a value that breaks its trimmed rule with INVALID_ARGUMENT.", output, StringComparison.Ordinal);
    }

    private const string OwnValidate = """
        syntax = "proto2";

        package buf.validate;

        import "google/protobuf/descriptor.proto";

        extend google.protobuf.MessageOptions {
          optional MessageRules message = 1159;
        }

        extend google.protobuf.FieldOptions {
          optional FieldRules field = 1159;
        }

        message MessageRules {
          optional bool sealed = 99;
        }

        message FieldRules {
          optional bool trimmed = 99;
        }
        """;

    private const string Asks = """
        syntax = "proto3";

        package acme.asks.v1;

        import "buf/validate/validate.proto";

        service AskService {
          rpc Ask(AskRequest) returns (AskResponse);
        }

        message AskRequest {
          option (buf.validate.message).sealed = true;
          string text = 1 [(buf.validate.field).trimmed = true];
        }

        message AskResponse {}
        """;

    [Theory]
    [InlineData("--format", "yaml", "--format takes text or json")]
    [InlineData("--only", "fields", "--only takes calls or rules")]
    [InlineData("--only", null, "--only takes calls or rules")]
    [InlineData("--verbose", null, "unknown option '--verbose'")]
    public void Cases_BadOption_ExitsTwo(string option, string? value, string message)
    {
        string[] args = ["cases", Module.Value, option, .. value is null ? [] : new[] { value }];

        var (exitCode, output) = Buf.Tool(args);

        Assert.Equal((2, true), (exitCode, output.Contains(message, StringComparison.Ordinal)));
    }

    [Fact]
    public void Cases_NoDirectory_ExitsTwo()
    {
        var (exitCode, output) = Buf.Tool("cases", "missing");

        Assert.Equal((2, true), (exitCode, output.Contains("no directory at missing", StringComparison.Ordinal)));
    }

    [Fact]
    public void Cases_NoProtoFiles_ExitsOne()
    {
        var (exitCode, output) = Buf.Tool("cases", Write());

        Assert.Equal((1, true), (exitCode, output.Contains("the directory holds no .proto files", StringComparison.Ordinal)));
    }

    // buf's errors name the file where the reader keeps it, not where the tool compiled it.
    [Fact]
    public void Cases_ContractThatDoesntCompile_ExitsOneWithBufsErrors()
    {
        var module = Write(("acme/notes/v1/notes.proto", "syntax = \"proto3\";\nmessage {\n"));

        var (exitCode, output) = Buf.Tool("cases", module);

        Assert.Equal(1, exitCode);
        Assert.Contains($"dt-protobuf cases: {module}/acme/notes/v1/notes.proto:2:1:missing name after `message`", output, StringComparison.Ordinal);
    }

    // A throwaway directory holding the files, each at its path.
    private static string Write(params (string Path, string Proto)[] files)
    {
        var module = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(module);
        foreach (var (path, proto) in files)
        {
            var file = Path.Combine(module, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, proto);
        }

        return module;
    }
}
