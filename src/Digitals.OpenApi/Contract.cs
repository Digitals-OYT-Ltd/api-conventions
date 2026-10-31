using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Digitals.OpenApi;

/// <summary>Reads an OpenAPI document from a JSON or YAML file, with every <c>$ref</c> resolved.</summary>
internal static class Contract
{
    /// <summary>The document, or the reasons it can't be read.</summary>
    public static (OpenApiDocument? Document, string[] Errors) Read(string path)
    {
        var (root, unreadable) = ContractYaml.Read(path);
        if (root is null)
        {
            return (null, unreadable);
        }

        try
        {
            var result = OpenApiYamlReader.Read(root, new Uri(Path.GetFullPath(path)), new OpenApiReaderSettings());
            var diagnostics = new[] { result.Diagnostic }.OfType<OpenApiDiagnostic>().ToArray();
            string[] errors = [.. diagnostics.SelectMany(d => d.Errors).Select(e => e.ToString())];
            if (errors.Length > 0)
            {
                return (null, errors);
            }

            if (diagnostics.Any(d => d.SpecificationVersion == OpenApiSpecVersion.OpenApi2_0))
            {
                DropBodiesWithoutSchema(result.Document);
            }

            return (result.Document, []);
        }
        catch (OpenApiUnsupportedSpecVersionException e)
        {
            // The reader throws, not reports, for an openapi version it doesn't know.
            return (null, [e.Message]);
        }
        catch (FormatException e)
        {
            // And for a number keyword such as minLength with something else in it.
            return (null, [$"a schema keyword that takes a number has something else: {e.Message}"]);
        }
    }

    // In Swagger 2.0 a response without a schema has no body, but the reader gives it one of each type it produces.
    private static void DropBodiesWithoutSchema(OpenApiDocument? document)
    {
        var bodies = new[] { document }.OfType<OpenApiDocument>()
            .SelectMany(d => d.Paths.Values)
            .SelectMany(p => p.Operations.OrEmpty())
            .SelectMany(o => o.Value.Responses.OrEmpty())
            .Select(r => r.Value.Content)
            .OfType<IDictionary<string, IOpenApiMediaType>>();
        foreach (var content in bodies)
        {
            foreach (var media in content.Where(c => c.Value.Schema is null).Select(c => c.Key).ToList())
            {
                content.Remove(media);
            }
        }
    }
}
