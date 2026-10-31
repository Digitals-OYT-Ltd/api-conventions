using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using static Digitals.Sentences;

namespace Digitals;

/// <summary>What breaking a rule means for the input: left out, unreadable, or read but invalid.</summary>
internal enum RuleKind
{
    /// <summary>A required field or parameter is missing.</summary>
    Missing,

    /// <summary>The input can't be read: a value of the wrong type, a field the schema doesn't define.</summary>
    Unreadable,

    /// <summary>The input is read but breaks a rule: a length, a range, a format.</summary>
    Invalid,
}

/// <summary>One rule on an input, and the values that show it.</summary>
/// <param name="Field">The field's path, such as <c>host.role</c> or <c>tags[]</c>; empty for the whole input.</param>
/// <param name="Name">The schema keyword, such as <c>maxLength</c>.</param>
/// <param name="Kind">What breaking it means for the input.</param>
/// <param name="ReadOnly">Whether the field, or one it sits in, is read-only.</param>
/// <param name="Detail">What it accepts and rejects, as a sentence without its full stop.</param>
internal sealed record SchemaRule(string Field, string Name, RuleKind Kind, bool ReadOnly, string Detail)
{
    /// <summary>The rule's ID under <paramref name="location"/>: location.field.rule, or location.rule for the root.</summary>
    public string Id(string location) => Field.Length == 0 ? $"{location}.{Name}" : $"{location}.{Field}.{Name}";
}

/// <summary>
/// Walks a schema field by field and lists a rule for every keyword a test can break: lengths, bounds, formats,
/// patterns, enums, items, properties and variants. Shared by dt-openapi and dt-asyncapi.
/// </summary>
/// <param name="Noun">What carries the input, for "Rejects a request without name".</param>
/// <param name="Whole">The whole input, for "Rejects a body that matches none of its variants".</param>
/// <param name="WrongTypes">The value of the wrong type to send for each type, <see cref="JsonWrongTypes"/> or
/// <see cref="TextWrongTypes"/>.</param>
internal sealed record SchemaRules(
    string Noun,
    string Whole,
    IReadOnlyDictionary<JsonSchemaType, string> WrongTypes)
{
    /// <summary>The wrong type to send in a JSON input.</summary>
    public static readonly IReadOnlyDictionary<JsonSchemaType, string> JsonWrongTypes =
        new Dictionary<JsonSchemaType, string>
        {
            [JsonSchemaType.String] = "42",
            [JsonSchemaType.Integer] = "\"abc\"",
            [JsonSchemaType.Number] = "\"abc\"",
            [JsonSchemaType.Boolean] = "\"yes\"",
            [JsonSchemaType.Array] = "an object",
            [JsonSchemaType.Object] = "a list",
        };

    /// <summary>
    /// The wrong type to send in a URL or a form. Every value there is text, so only a number or a boolean can be
    /// the wrong type.
    /// </summary>
    public static readonly IReadOnlyDictionary<JsonSchemaType, string> TextWrongTypes =
        new Dictionary<JsonSchemaType, string>
        {
            [JsonSchemaType.Integer] = "abc",
            [JsonSchemaType.Number] = "abc",
            [JsonSchemaType.Boolean] = "maybe",
        };

    /// <summary>Every rule on <paramref name="field"/> and the fields inside it.</summary>
    public IEnumerable<SchemaRule> Walk(string field, IOpenApiSchema? schema, bool required, JsonNode? example) =>
        Walk(field, schema, required, example, [], readOnly: false);

    private IEnumerable<SchemaRule> Walk(
        string field,
        IOpenApiSchema? schema,
        bool required,
        JsonNode? example,
        HashSet<string> seen,
        bool readOnly)
    {
        var place = new Place(field, readOnly);
        if (required)
        {
            yield return place.Rule("required", RuleKind.Missing, $"Rejects a {Noun} without {field}");
        }

        // A schema that refers back to itself is walked once on each branch.
        if (schema is null || schema is OpenApiSchemaReference { Reference.Id: { } id } && !seen.Add(id))
        {
            yield break;
        }

        var facets = Facets(schema).ToArray();
        var cases = Scalar(place, facets, new[] { example, Example(facets) }.FirstOrDefault(e => e is not null))
            .Concat(Array(place, facets, seen))
            .Concat(Object(place, facets, seen))
            .Concat(Variants(place, facets, seen));
        foreach (var found in cases)
        {
            yield return found;
        }
    }

    private IEnumerable<SchemaRule> Scalar(Place place, IOpenApiSchema[] facets, JsonNode? example)
    {
        var type = First(facets, f => f.Type) & ~JsonSchemaType.Null;
        if (WrongTypes.GetValueOrDefault(type.GetValueOrDefault()) is { } wrong)
        {
            yield return place.Rule("type", RuleKind.Unreadable, $"Rejects {wrong}");
        }

        var cases = Text(place, facets, example).Concat(Number(place, facets, type)).Concat(Enum(place, facets));
        foreach (var found in cases)
        {
            yield return found;
        }
    }

    // An enum already names every value, so a length or pattern adds nothing.
    private static IEnumerable<SchemaRule> Text(Place place, IOpenApiSchema[] facets, JsonNode? example)
    {
        if (Values(facets).Count > 0)
        {
            yield break;
        }

        var unit = First(facets, f => f.Format) == "binary" ? "byte" : "character";
        if (First(facets, f => f.MinLength) is { } min and > 0)
        {
            var fewer = min == 1 ? "an empty value" : $"{min - 1}";
            yield return place.Rule("minLength", RuleKind.Invalid, $"Accepts {Count(min, unit)}, rejects {fewer}");
        }

        if (First(facets, f => f.MaxLength) is { } max)
        {
            yield return place.Rule("maxLength", RuleKind.Invalid, $"Accepts {Count(max, unit)}, rejects {max + 1}");
        }

        if (First(facets, f => f.Pattern) is { } pattern)
        {
            yield return place.Rule("pattern", RuleKind.Invalid, Pattern(pattern, example));
        }

        var maximum = First(facets, f => f.Maximum);
        if (First(facets, f => f.Format) is { } format && BadFormat(format, maximum) is { } bad)
        {
            yield return place.Rule("format", RuleKind.Invalid, $"Rejects {bad}");
        }
    }

    private static IEnumerable<SchemaRule> Number(Place place, IOpenApiSchema[] facets, JsonSchemaType? type)
    {
        if (type is not (JsonSchemaType.Integer or JsonSchemaType.Number))
        {
            yield break;
        }

        // The next value past a bound: the multiple when there is one, else 1 for an integer, else one
        // decimal place finer than the bound, so 0.5 steps to 0.49.
        var multiple = First(facets, f => f.MultipleOf);
        decimal Step(decimal bound) =>
            multiple ?? (type == JsonSchemaType.Integer ? 1 : Finer(bound));

        if (Decimal(First(facets, f => f.Minimum)) is { } min)
        {
            var detail = $"Accepts {Show(min)}, rejects {Show(min - Step(min))}";
            yield return place.Rule("minimum", RuleKind.Invalid, detail);
        }

        if (Decimal(First(facets, f => f.ExclusiveMinimum)) is { } above)
        {
            var detail = $"Accepts {Show(above + Step(above))}, rejects {Show(above)}";
            yield return place.Rule("exclusiveMinimum", RuleKind.Invalid, detail);
        }

        if (Decimal(First(facets, f => f.Maximum)) is { } max)
        {
            var detail = $"Accepts {Show(max)}, rejects {Show(max + Step(max))}";
            yield return place.Rule("maximum", RuleKind.Invalid, detail);
        }

        if (Decimal(First(facets, f => f.ExclusiveMaximum)) is { } below)
        {
            var detail = $"Accepts {Show(below - Step(below))}, rejects {Show(below)}";
            yield return place.Rule("exclusiveMaximum", RuleKind.Invalid, detail);
        }

        // Every integer is a multiple of 1, and an integer off a multiple of 5 is 6, not 7.5.
        if (multiple is { } of && !(type == JsonSchemaType.Integer && of == 1))
        {
            var off = type == JsonSchemaType.Integer ? of + 1 : of * 1.5m;
            yield return place.Rule("multipleOf", RuleKind.Invalid, $"Accepts {Show(of)}, rejects {Show(off)}");
        }
    }

    private static IEnumerable<SchemaRule> Enum(Place place, IOpenApiSchema[] facets)
    {
        if (Values(facets) is { Count: > 0 } values)
        {
            string[] shown = [.. values.Select(v => IsNull(v) ? "null" : v.ToJsonString())];
            var accepts = shown.Length > 5 ? $"each of its {shown.Length} values" : And(shown);
            yield return place.Rule("enum", RuleKind.Invalid, $"Accepts {accepts}, rejects {Unlisted(values)}");
        }

        if (First(facets, f => f.Const) is { } constant)
        {
            var detail = $"Accepts {Show(constant)}, rejects {Show($"not-{constant}")}";
            yield return place.Rule("const", RuleKind.Invalid, detail);
        }
    }

    private IEnumerable<SchemaRule> Array(Place place, IOpenApiSchema[] facets, HashSet<string> seen)
    {
        if (First(facets, f => f.MinItems) is { } min and > 0)
        {
            var fewer = min == 1 ? "an empty list" : $"{min - 1}";
            yield return place.Rule("minItems", RuleKind.Invalid, $"Accepts {Count(min, "item")}, rejects {fewer}");
        }

        if (First(facets, f => f.MaxItems) is { } max)
        {
            yield return place.Rule("maxItems", RuleKind.Invalid, $"Accepts {Count(max, "item")}, rejects {max + 1}");
        }

        if (First(facets, f => f.UniqueItems) == true)
        {
            yield return place.Rule("uniqueItems", RuleKind.Invalid, "Rejects two equal items");
        }

        var items = Walk($"{place.Field}[]", First(facets, f => f.Items), false, null, [.. seen], place.ReadOnly);
        foreach (var found in items)
        {
            yield return found;
        }
    }

    private IEnumerable<SchemaRule> Object(Place place, IOpenApiSchema[] facets, HashSet<string> seen)
    {
        if (First(facets, f => f.MinProperties) is { } min and > 0)
        {
            var fewer = min == 1 ? "an empty object" : $"{min - 1}";
            var detail = $"Accepts {Count(min, "field")}, rejects {fewer}";
            yield return place.Rule("minProperties", RuleKind.Invalid, detail);
        }

        if (First(facets, f => f.MaxProperties) is { } max)
        {
            var detail = $"Accepts {Count(max, "field")}, rejects {max + 1}";
            yield return place.Rule("maxProperties", RuleKind.Invalid, detail);
        }

        if (facets.Any(f => f.Properties?.Count > 0 && !f.AdditionalPropertiesAllowed))
        {
            var detail = "Rejects a field the schema doesn't define";
            yield return place.Rule("additionalProperties", RuleKind.Unreadable, detail);
        }

        var required = facets.SelectMany(f => f.Required.OrEmpty()).ToHashSet(StringComparer.Ordinal);
        var properties = facets.SelectMany(f => f.Properties.OrEmpty()).DistinctBy(p => p.Key);
        foreach (var (name, property) in properties)
        {
            var path = place.Field.Length == 0 ? name : $"{place.Field}.{name}";
            var readOnly = place.ReadOnly || property.ReadOnly;
            foreach (var found in Walk(path, property, required.Contains(name), example: null, [.. seen], readOnly))
            {
                yield return found;
            }
        }
    }

    // Each variant's fields are walked under the same names; a rule two variants share is one case.
    private IEnumerable<SchemaRule> Variants(Place place, IOpenApiSchema[] facets, HashSet<string> seen)
    {
        var label = place.Field.Length == 0 ? Whole : $"a value for {place.Field}";
        var kinds = new[] { ("oneOf", First(facets, f => f.OneOf)), ("anyOf", First(facets, f => f.AnyOf)) };
        foreach (var (keyword, variants) in kinds.Where(k => k.Item2?.Count > 0))
        {
            var detail = $"Rejects {label} that matches none of its {variants.OrEmpty().Count()} variants";
            yield return place.Rule(keyword, RuleKind.Invalid, detail);
            var cases = variants.OrEmpty()
                .SelectMany(v => Walk(place.Field, v, required: false, example: null, [.. seen], place.ReadOnly));
            foreach (var found in cases)
            {
                yield return found;
            }
        }
    }

    /// <summary>A field being walked, and whether it is read-only.</summary>
    private sealed record Place(string Field, bool ReadOnly)
    {
        public SchemaRule Rule(string name, RuleKind kind, string detail) => new(Field, name, kind, ReadOnly, detail);
    }

    // A schema and the allOf parts it's made of. A keyword takes its first value across them.
    private static IEnumerable<IOpenApiSchema> Facets(IOpenApiSchema schema) =>
        [schema, .. schema.AllOf.OrEmpty().SelectMany(Facets)];

    private static T? First<T>(IOpenApiSchema[] facets, Func<IOpenApiSchema, T?> keyword) =>
        facets.Select(keyword).FirstOrDefault(value => value is not null);

    private static IList<JsonNode> Values(IOpenApiSchema[] facets) =>
        First(facets, f => f.Enum?.Count > 0 ? f.Enum : null) ?? [];

#pragma warning disable CS0618 // Microsoft.OpenApi reads `example` into Example, which it marks obsolete.
    private static JsonNode? Example(IOpenApiSchema[] facets) =>
        First(facets, f => f.Examples?.FirstOrDefault() ?? f.Example);
#pragma warning restore CS0618

    private static string Pattern(string pattern, JsonNode? example)
    {
        var good = example is JsonValue value && value.TryGetValue(out string? text) && Matches(text, pattern) == true
            ? Show(text)
            : $"a value matching {pattern}";
        var bad = Candidates.FirstOrDefault(c => Matches(c, pattern) == false);
        return bad is null ? $"Accepts {good}, rejects a value that breaks it" : $"Accepts {good}, rejects {Show(bad)}";
    }

    // Values that break most patterns. The first one the pattern rejects is the one to send.
    private static readonly string[] Candidates = ["!", " ", "", "a", "0", "\t", "\u0001"];

    // Null when .NET can't run the pattern, which then gets no suggested value.
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

    // A value each format rejects.
    private static readonly Dictionary<string, string> BadFormats = new(StringComparer.Ordinal)
    {
        ["uuid"] = "\"not-a-uuid\"",
        ["date"] = "\"2026-02-30\"",
        ["date-time"] = "\"2026-02-30T10:00:00Z\"",
        ["time"] = "\"25:00:00\"",
        ["email"] = "\"no-at-sign\"",
        ["uri"] = "\"not a uri\"",
        ["hostname"] = "\"-bad-.example\"",
        ["ipv4"] = "\"256.0.0.1\"",
        ["ipv6"] = "\"::g\"",
        ["duration"] = "\"P1X\"",
        ["byte"] = "\"!!\"",
    };

    // An integer format only when no maximum sets a tighter bound.
    private static string? BadFormat(string format, string? maximum) =>
        BadFormats.TryGetValue(format, out var bad) ? bad : (format, maximum) switch
        {
            ("int32", null) => "2147483648 (past int32)",
            ("int64", null) => "9223372036854775808 (past int64)",
            _ => null,
        };

    private static string Unlisted(IList<JsonNode> values)
    {
        var numbers = values.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.Number).ToArray();
        return numbers.Length == values.Count
            ? Show(numbers.Max(v => v.GetValue<decimal>()) + 1)
            : Show($"not-{values.Where(v => !IsNull(v)).Select(v => v.ToString()).FirstOrDefault()}");
    }

    // Microsoft.OpenApi keeps a null in an enum as a sentinel string.
    private static bool IsNull(JsonNode value) => JsonNullSentinel.IsJsonNullSentinel(value);

    // 1 in the decimal place after the bound's last one, and never coarser than 0.01.
    private static decimal Finer(decimal bound) =>
        new(1, 0, 0, isNegative: false, (byte)(Math.Max(bound.Scale, (byte)1) + 1));

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Show(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Show(string value) => JsonSerializer.Serialize(value, Relaxed);

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>Null-safe enumeration for the models' nullable collections.</summary>
internal static class Enumerables
{
    /// <summary>The items, or none when there's no collection.</summary>
    public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? items) => items ?? [];
}
