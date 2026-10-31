using System.Text.Json.Nodes;
using Microsoft.OpenApi.YamlReader;
using SharpYaml.Serialization;

namespace Digitals;

/// <summary>
/// Reads a JSON or YAML contract into a JSON tree for the <c>cases</c> commands. Shared by dt-openapi and
/// dt-asyncapi.
/// </summary>
internal static class ContractYaml
{
    /// <summary>The document's root object, or the reasons it can't be read.</summary>
    public static (JsonObject? Root, string[] Errors) Read(string path)
    {
        var yaml = new YamlStream();
        try
        {
            using var reader = File.OpenText(path);
            yaml.Load(reader);
        }
        catch (SharpYaml.YamlException e)
        {
            return (null, [e.Message]);
        }

        if (yaml.Documents.FirstOrDefault() is not { } document
            || YamlConverter.ToJsonNode(document) is not JsonObject root)
        {
            return (null, ["the file holds no YAML or JSON object"]);
        }

        Merge(root);
        return (root, []);
    }

    // SharpYaml expands an alias but reads a merge key (<<: *name) as a key called "<<". Our contracts reuse a
    // schema with one, so fold it in the way YAML 1.1 does: a key the node already has wins.
    private static void Merge(JsonNode? node)
    {
        foreach (var child in Children(node))
        {
            Merge(child);
        }

        if (node is JsonObject target && target.Remove("<<", out var merged))
        {
            IEnumerable<JsonNode?> sources = merged is JsonArray many ? many : [merged];
            foreach (var source in sources.OfType<JsonObject>())
            {
                Fold(target, source.DeepClone().AsObject());
            }
        }
    }

    private static IEnumerable<JsonNode?> Children(JsonNode? node) => node switch
    {
        JsonObject o => o.Select(p => p.Value),
        JsonArray a => a,
        _ => [],
    };

    private static void Fold(JsonObject target, JsonObject source)
    {
        foreach (var key in source.Select(p => p.Key).Where(k => !target.ContainsKey(k)).ToList())
        {
            source.Remove(key, out var value);
            target[key] = value;
        }
    }
}
