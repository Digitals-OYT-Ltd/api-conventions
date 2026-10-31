using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Digitals.Sentences;

namespace Digitals.Protobuf;

/// <summary>One protovalidate rule on a request, and the values that show it.</summary>
/// <param name="Field">The field's path, such as <c>booking.venue_id</c> or <c>tags[]</c>; empty for the request.</param>
/// <param name="Name">The rule as the .proto names it, such as <c>max_len</c>, or a CEL rule's ID.</param>
/// <param name="Detail">What it accepts and rejects, as a sentence without its full stop.</param>
internal sealed record FieldRule(string Field, string Name, string Detail)
{
    /// <summary>field.rule, or the rule alone for one on the request.</summary>
    public string Id => Field.Length == 0 ? Name : $"{Field}.{Name}";
}

/// <summary>
/// Walks a request message field by field and lists a rule for every protovalidate rule a test can break:
/// lengths, bounds, formats, patterns, enums, items, pairs, oneofs and CEL rules. A rule it doesn't know gets a
/// case that names it, so none is dropped.
/// </summary>
internal sealed partial class Protovalidate(Contract contract)
{
    private const string FieldOption = "[buf.validate.field]";

    /// <summary>Every rule on <paramref name="message"/> and the messages inside it.</summary>
    public IEnumerable<FieldRule> Walk(string message) => Message(message, "", []);

    // A message that holds itself is walked once on each branch.
    private IEnumerable<FieldRule> Message(string type, string prefix, HashSet<string> seen)
    {
        if (!contract.Messages.TryGetValue(type, out var message) || !seen.Add(type))
        {
            return [];
        }

        var field = prefix.TrimEnd('.');
        var fields = Contract.Objects(message["field"]).ToArray();
        return MessageRules(field, message["options"]?["[buf.validate.message]"] as JsonObject)
            .Concat(Oneofs(prefix, message, fields))
            .Concat(fields.SelectMany(f => Field(prefix, f, seen)));
    }

    private static IEnumerable<FieldRule> MessageRules(string field, JsonObject? rules)
    {
        foreach (var (key, value) in rules ?? [])
        {
            var found = key switch
            {
                "cel" => Cel(field, value),
                "celExpression" => Expressions(field, value),
                "oneof" => MessageOneofs(field, value),
                _ => [Unknown(field, key)],
            };
            foreach (var rule in found)
            {
                yield return rule;
            }
        }
    }

    private static IEnumerable<FieldRule> MessageOneofs(string field, JsonNode? value) =>
        Contract.Objects(value).Select((oneof, i) =>
        {
            string[] names = [.. ((JsonArray)oneof["fields"]!).Select(f => Within(field, (string)f!))];
            var detail = (bool?)oneof["required"] == true
                ? $"Rejects a request that sets none, or more than one, of {And(names)}"
                : $"Rejects a request that sets more than one of {And(names)}";
            return new FieldRule(field, Numbered("oneof", i), detail);
        });

    private static IEnumerable<FieldRule> Oneofs(string prefix, JsonObject message, JsonObject[] fields) =>
        Contract.Objects(message["oneofDecl"]).Select((oneof, i) => (oneof, i))
            .Where(o => (bool?)o.oneof["options"]?["[buf.validate.oneof]"]?["required"] == true)
            .Select(o =>
            {
                string[] names = [.. fields.Where(f => (int?)f["oneofIndex"] == o.i).Select(f => prefix + Name(f))];
                var detail = $"Rejects a request that sets none of {Join(names, "or")}";
                return new FieldRule(prefix + Name(o.oneof), "required", detail);
            });

    private IEnumerable<FieldRule> Field(string prefix, JsonObject field, HashSet<string> seen)
    {
        var path = prefix + Name(field);
        var rules = Rules(path, field["options"]?[FieldOption] as JsonObject, field);
        if ((string?)field["typeName"] is not { } type || !contract.Messages.ContainsKey(type))
        {
            return rules;
        }

        if (MapEntry(field) is [_, var entry])
        {
            return (string?)entry["typeName"] is { } valueType
                ? rules.Concat(Message(valueType, $"{path}.values.", [.. seen]))
                : rules;
        }

        var inside = (string?)field["label"] == "LABEL_REPEATED" ? $"{path}[]." : $"{path}.";
        return rules.Concat(Message(type, inside, [.. seen]));
    }

    // The rules on one field, or on a list's items or a map's keys and values, which hold the same rules.
    private IEnumerable<FieldRule> Rules(string path, JsonObject? rules, JsonObject field)
    {
        foreach (var (key, value) in rules ?? [])
        {
            var found = key switch
            {
                "required" => (bool?)value == true ? [new(path, "required", $"Rejects a request without {path}")] : [],
                "ignore" => [],
                "cel" => Cel(path, value),
                "celExpression" => Expressions(path, value),
                "repeated" => Repeated(path, value!.AsObject(), field),
                "map" when MapEntry(field) is [var mapKey, var mapValue] =>
                    Map(path, value!.AsObject(), mapKey, mapValue),
                _ when Scales.ContainsKey(key) || Typed.Contains(key) => Kind(path, key, value!.AsObject(), field),
                _ => [Unknown(path, key)],
            };
            foreach (var rule in found)
            {
                yield return rule;
            }
        }
    }

    private static readonly HashSet<string> Typed =
        new(["string", "bytes", "bool", "enum", "any", "fieldMask"], StringComparer.Ordinal);

    private static IEnumerable<FieldRule> Cel(string field, JsonNode? value) =>
        Contract.Objects(value).Select((rule, i) =>
        {
            var id = (string?)rule["id"] is { } named ? named : Numbered("cel", i);
            var says = (string?)rule["message"] ?? (string)rule["expression"]!;
            var subject = field.Length == 0 ? "a request that" : $"a request whose {field}";
            return new FieldRule(field, id, $"Rejects {subject} breaks {id} ({Show(says)})");
        });

    private static IEnumerable<FieldRule> Expressions(string field, JsonNode? value) =>
        ((JsonArray)value!).Select((expression, i) =>
        {
            var subject = field.Length == 0 ? "a request that" : $"a request whose {field}";
            var detail = $"Rejects {subject} breaks the rule {Show(expression!.GetValue<string>())}";
            return new FieldRule(field, Numbered("cel_expression", i), detail);
        });

    private IEnumerable<FieldRule> Repeated(string path, JsonObject rules, JsonObject field)
    {
        foreach (var (key, value) in rules)
        {
            var found = key switch
            {
                "minItems" => [Fewest(path, key, (int)Number(value!), "item", "an empty list")],
                "maxItems" => [Most(path, key, (int)Number(value!), "item")],
                "unique" => [new(path, "unique", "Rejects two equal items")],
                "items" => Rules($"{path}[]", value!.AsObject(), field),
                _ => [Unknown(path, key)],
            };
            foreach (var rule in found)
            {
                yield return rule;
            }
        }
    }

    // A map's entry is a message of its own, with a key and a value field.
    private JsonObject[] MapEntry(JsonObject field) =>
        (string?)field["typeName"] is { } type
        && contract.Messages.TryGetValue(type, out var entry)
        && (bool?)entry["options"]?["mapEntry"] == true
            ? [.. Contract.Objects(entry["field"])]
            : [];

    private IEnumerable<FieldRule> Map(string path, JsonObject rules, JsonObject mapKey, JsonObject mapValue)
    {
        foreach (var (key, value) in rules)
        {
            var found = key switch
            {
                "minPairs" => [Fewest(path, key, (int)Number(value!), "pair", "an empty map")],
                "maxPairs" => [Most(path, key, (int)Number(value!), "pair")],
                "keys" => Rules($"{path}.keys", value!.AsObject(), mapKey),
                "values" => Rules($"{path}.values", value!.AsObject(), mapValue),
                _ => [Unknown(path, key)],
            };
            foreach (var rule in found)
            {
                yield return rule;
            }
        }
    }

    // The rules for one type: string.max_len, int32.gte and so on. A false flag, such as uuid: false, checks
    // nothing, and an example is a value to accept, not a rule.
    private IEnumerable<FieldRule> Kind(string path, string kind, JsonObject rules, JsonObject field) =>
        rules.Where(r => r.Key is not ("example" or "strict") && r.Value!.GetValueKind() != JsonValueKind.False)
            .Select(r => Detail(kind, r.Key, r.Value!, field) is { } detail
                ? new FieldRule(path, Snake(r.Key), detail)
                : Unknown(path, r.Key));

    private string? Detail(string kind, string rule, JsonNode value, JsonObject field) => kind switch
    {
        "string" => Text(Strings, rule, value),
        "bytes" => Text(Bytes, rule, value),
        "bool" => rule == "const" ? $"Accepts {value.ToJsonString()}, rejects {JsonValue.Create(!(bool)value)}" : null,
        "enum" => Enum(rule, value, field),
        "any" => Listed(rule, value, "type"),
        "fieldMask" => Listed(rule, value, "path"),
        _ => Bounded(Scales[kind], rule, value),
    };

    /// <summary>How to word a string's or a bytes field's rules.</summary>
    /// <param name="Unit">What len, min_len and max_len count.</param>
    /// <param name="Quote">A value as a test sends it: quoted text, or hex for bytes.</param>
    /// <param name="Other">A value other than the one given.</param>
    /// <param name="Formats">A value each format rule rejects.</param>
    private sealed record TextKind(
        string Unit,
        Func<string, string> Quote,
        Func<string, string> Other,
        Dictionary<string, string> Formats);

    private static string? Text(TextKind kind, string rule, JsonNode value)
    {
        if (TextRules.TryGetValue(rule, out var detail))
        {
            return detail(kind, value);
        }

        return kind.Formats.TryGetValue(rule, out var bad) ? $"Rejects {bad}" : null;
    }

    // A string's len_bytes, min_bytes and max_bytes count bytes, not characters.
    private static readonly Dictionary<string, Func<TextKind, JsonNode, string>> TextRules =
        new(StringComparer.Ordinal)
        {
            ["len"] = (kind, value) => Exactly(value, kind.Unit),
            ["lenBytes"] = (_, value) => Exactly(value, "byte"),
            ["minLen"] = (kind, value) => Fewest("", "", (int)Number(value), kind.Unit, "an empty value").Detail,
            ["minBytes"] = (_, value) => Fewest("", "", (int)Number(value), "byte", "an empty value").Detail,
            ["maxLen"] = (kind, value) => Most("", "", (int)Number(value), kind.Unit).Detail,
            ["maxBytes"] = (_, value) => Most("", "", (int)Number(value), "byte").Detail,
            ["pattern"] = (_, value) => Pattern(value.GetValue<string>()),
            ["prefix"] = (kind, value) => $"Rejects a value that doesn't start with {Quoted(kind, value)}",
            ["suffix"] = (kind, value) => $"Rejects a value that doesn't end with {Quoted(kind, value)}",
            ["contains"] = (kind, value) => $"Rejects a value without {Quoted(kind, value)}",
            ["notContains"] = (kind, value) => $"Rejects a value containing {Quoted(kind, value)}",
            ["const"] = (kind, value) =>
                $"Accepts {Quoted(kind, value)}, rejects {kind.Other(value.GetValue<string>())}",
            ["in"] = (kind, value) =>
                $"Accepts {Values(value, kind.Quote)}, rejects {kind.Other(value[0]!.GetValue<string>())}",
            ["notIn"] = (kind, value) => $"Rejects {Values(value, kind.Quote)}",
            ["wellKnownRegex"] = (_, value) => $"Rejects a value that isn't a valid {Words(value.GetValue<string>())}",
        };

    private static string Quoted(TextKind kind, JsonNode value) => kind.Quote(value.GetValue<string>());

    private static string Exactly(JsonNode value, string unit) =>
        $"Accepts {Count((int)Number(value), unit)}, rejects {Number(value) + 1}";

    private string? Enum(string rule, JsonNode value, JsonObject field)
    {
        var values = Contract.Objects(contract.Enums.GetValueOrDefault((string?)field["typeName"] ?? "")?["value"])
            .ToDictionary(v => (int)v["number"]!, v => (string)v["name"]!);
        string Named(int number) => values.GetValueOrDefault(number, $"{number}");
        var undefined = values.Keys.DefaultIfEmpty(0).Max() + 1;
        int[] listed = value is JsonArray array ? [.. array.Select(v => (int)v!)] : [];
        return rule switch
        {
            "const" => $"Accepts {Named((int)value)}, rejects any other value",
            "definedOnly" => $"Rejects the undefined number {undefined}",
            "in" => $"Accepts {And([.. listed.Select(Named)])}, " +
                $"rejects {Named(values.Keys.Except(listed).DefaultIfEmpty(undefined).First())}",
            "notIn" => $"Rejects {And([.. listed.Select(Named)])}",
            _ => null,
        };
    }

    private static string? Listed(string rule, JsonNode value, string noun) => rule switch
    {
        "const" => $"Rejects a {noun} list other than {Show(value.GetValue<string>())}",
        "in" => $"Rejects a {noun} other than {Values(value, Show)}",
        "notIn" => $"Rejects {Values(value, Show)}",
        _ => null,
    };

    // A float bound can be inf or nan, which has no next value.
    private static string? Bounded(Scale scale, string rule, JsonNode value) =>
        Finite(value) && BoundRules.TryGetValue(rule, out var detail) ? detail(scale, value) : null;

    private static readonly Dictionary<string, Func<Scale, JsonNode, string>> BoundRules = new(StringComparer.Ordinal)
    {
        ["const"] = (scale, value) => Bound(scale, value, accepts: 0, rejects: 1),
        ["lte"] = (scale, value) => Bound(scale, value, accepts: 0, rejects: 1),
        ["lt"] = (scale, value) => Bound(scale, value, accepts: -1, rejects: 0),
        ["gt"] = (scale, value) => Bound(scale, value, accepts: 1, rejects: 0),
        ["gte"] = (scale, value) => Bound(scale, value, accepts: 0, rejects: -1),
        ["in"] = (scale, value) => In(scale, (JsonArray)value),
        ["notIn"] = (_, value) => $"Rejects {Values(value, v => v)}",
        ["finite"] = (_, _) => "Rejects infinity and NaN",
        ["ltNow"] = (_, _) => "Rejects a time in the future",
        ["gtNow"] = (_, _) => "Rejects a time in the past",
        ["within"] = (_, value) => $"Rejects a time more than {value.GetValue<string>()} from now",
    };

    // The bound, or the value a step past it either way: 1 for an integer or whole seconds, else one decimal place
    // finer.
    private static string Bound(Scale scale, JsonNode value, int accepts, int rejects)
    {
        var bound = scale.Value(value);
        var step = Step(bound, scale.Whole);
        return $"Accepts {scale.Format(bound + (accepts * step))}, rejects {scale.Format(bound + (rejects * step))}";
    }

    private static string In(Scale scale, JsonArray values)
    {
        var max = values.Max(v => scale.Value(v!));
        return $"Accepts {Values(values, v => v)}, rejects {scale.Format(max + Step(max, scale.Whole))}";
    }

    private static bool Finite(JsonNode value) => value is JsonArray values
        ? values.All(v => Finite(v!))
        : value.GetValueKind() != JsonValueKind.String || !NotFinite.Contains(value.GetValue<string>());

    private static readonly HashSet<string> NotFinite = new(["Infinity", "-Infinity", "NaN"], StringComparer.Ordinal);

    private static decimal Step(decimal bound, bool whole) =>
        whole && bound == decimal.Truncate(bound)
            ? 1
            : new decimal(1, 0, 0, isNegative: false, (byte)(Math.Max(bound.Scale, (byte)1) + 1));

    /// <summary>How to read, show and step one kind of bound.</summary>
    /// <param name="Value">The bound as a number: itself, seconds, or seconds since 1970.</param>
    /// <param name="Format">The number as the .proto writes it.</param>
    /// <param name="Whole">Whether a whole bound steps by 1.</param>
    private sealed record Scale(Func<JsonNode, decimal> Value, Func<decimal, string> Format, bool Whole);

    private static readonly Scale Integer = new(Number, Plain, Whole: true);
    private static readonly Scale Float = new(Number, Plain, Whole: false);

    // A duration is "900s"; a timestamp is "2026-01-01T00:00:00Z".
    private static readonly Dictionary<string, Scale> Scales = new(StringComparer.Ordinal)
    {
        ["int32"] = Integer,
        ["int64"] = Integer,
        ["uint32"] = Integer,
        ["uint64"] = Integer,
        ["sint32"] = Integer,
        ["sint64"] = Integer,
        ["fixed32"] = Integer,
        ["fixed64"] = Integer,
        ["sfixed32"] = Integer,
        ["sfixed64"] = Integer,
        ["float"] = Float,
        ["double"] = Float,
        ["duration"] = new(v => Parse(v.GetValue<string>().TrimEnd('s')), v => $"{Plain(v)}s", Whole: true),
        ["timestamp"] = new(Seconds, Time, Whole: true),
    };

    // buf writes a 64-bit number as a string, so it keeps every digit.
    private static decimal Number(JsonNode value) => Parse(Raw(value));

    private static decimal Parse(string value) =>
        Trimmed(decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));

    private static string Plain(decimal value) => Trimmed(value).ToString(CultureInfo.InvariantCulture);

    // Without trailing zeros, so 1.500s steps to 1.51s, not 1.5001s, and 1.5 + 0.00 shows as 1.5.
    private static decimal Trimmed(decimal value) => value / 1.0000000000000000000000000000m;

    private static decimal Seconds(JsonNode value) =>
        DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture).ToUnixTimeMilliseconds() / 1000m;

    private static string Time(decimal seconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000))
            .ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture);

    private static FieldRule Fewest(string path, string rule, int min, string unit, string none)
    {
        var fewer = min <= 1 ? none : $"{min - 1}";
        return new(path, Snake(rule), $"Accepts {Count(min, unit)}, rejects {fewer}");
    }

    private static FieldRule Most(string path, string rule, int max, string unit) =>
        new(path, Snake(rule), $"Accepts {Count(max, unit)}, rejects {max + 1}");

    // A rule this walker doesn't know, such as a predefined one ([acme.notes.v1.even]), is named and left to
    // the reader.
    private static FieldRule Unknown(string path, string rule)
    {
        var name = rule.StartsWith('[') ? rule[(rule.LastIndexOf('.') + 1)..^1] : Snake(rule);
        return new(path, name, $"Rejects a value that breaks its {name} rule");
    }

    // "a", "a and b", or "each of its 7 values" past five.
    private static string Values(JsonNode value, Func<string, string> show)
    {
        string[] shown = [.. ((JsonArray)value).Select(v => show(Raw(v!)))];
        return shown.Length > 5 ? $"each of its {shown.Length} values" : And(shown);
    }

    private static string Raw(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();

    private static string Pattern(string pattern)
    {
        var bad = Candidates.FirstOrDefault(c => Matches(c, pattern) == false);
        var rejects = bad is null ? "a value that breaks it" : Show(bad);
        return $"Accepts a value matching {pattern}, rejects {rejects}";
    }

    // Values that break most patterns. The first one the pattern rejects is the one to send.
    private static readonly string[] Candidates = ["!", " ", "", "a", "0", "\t", "\u0001"];

    // Null when .NET can't run the pattern, which protovalidate writes in RE2.
    private static bool? Matches(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // A value each string format rejects.
    private static readonly Dictionary<string, string> BadStrings = new(StringComparer.Ordinal)
    {
        ["email"] = "\"no-at-sign\"",
        ["hostname"] = "\"-bad-.example\"",
        ["ip"] = "\"256.0.0.1\"",
        ["ipv4"] = "\"256.0.0.1\"",
        ["ipv6"] = "\"::g\"",
        ["uri"] = "\"not a uri\"",
        ["uriRef"] = "\"%zz\"",
        ["address"] = "\"-bad-.example\"",
        ["uuid"] = "\"not-a-uuid\"",
        ["tuuid"] = "\"not-a-uuid\"",
        ["ipWithPrefixlen"] = "\"10.0.0.1\"",
        ["ipv4WithPrefixlen"] = "\"10.0.0.1/33\"",
        ["ipv6WithPrefixlen"] = "\"::1/129\"",
        ["ipPrefix"] = "\"10.0.0.1/8\"",
        ["ipv4Prefix"] = "\"10.0.0.1/8\"",
        ["ipv6Prefix"] = "\"::1/64\"",
        ["hostAndPort"] = "\"example.com\"",
        ["ulid"] = "\"not-a-ulid\"",
        ["protobufFqn"] = "\".leading.dot\"",
        ["protobufDotFqn"] = "\"no.leading.dot\"",
    };

    // An address in bytes is 4 or 16 of them, and a UUID 16.
    private static readonly Dictionary<string, string> BadBytes = new(StringComparer.Ordinal)
    {
        ["ip"] = "3 bytes",
        ["ipv4"] = "16 bytes",
        ["ipv6"] = "4 bytes",
        ["uuid"] = "15 bytes",
    };

    // Declared after the formats they hold, which initialise first.
    private static readonly TextKind Strings = new("character", Show, v => Show($"not-{v}"), BadStrings);
    private static readonly TextKind Bytes = new("byte", ShowBytes, v => ShowBytes(v) + "00", BadBytes);

    // KNOWN_REGEX_HTTP_HEADER_NAME is an "HTTP header name".
    private static string Words(string known) =>
        known.Replace("KNOWN_REGEX_", "", StringComparison.Ordinal).Replace('_', ' ').ToLowerInvariant()
            .Replace("http", "HTTP", StringComparison.Ordinal);

    // buf writes bytes as base64, and a test sends them as hex.
    private static string ShowBytes(string base64) =>
        $"0x{Convert.ToHexString(Convert.FromBase64String(base64)).ToLowerInvariant()}";

    private static string Show(string value) => JsonSerializer.Serialize(value, Relaxed);

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Name(JsonObject node) => (string)node["name"]!;

    private static string Within(string field, string name) => field.Length == 0 ? name : $"{field}.{name}";

    // "oneof", then "oneof2" for a second one, so each keeps its own ID.
    private static string Numbered(string name, int index) => index == 0 ? name : $"{name}{index + 1}";

    // maxLen is max_len in the .proto.
    private static string Snake(string camel) => SnakeCase().Replace(camel, "_$1").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex SnakeCase();
}
