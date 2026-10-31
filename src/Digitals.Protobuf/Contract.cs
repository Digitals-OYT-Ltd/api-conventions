using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Digitals.Protobuf;

/// <summary>One RPC, as the <c>cases</c> command lists it.</summary>
/// <param name="Id">The service and the method, such as <c>BookingService.GetBooking</c>.</param>
/// <param name="Path">The gRPC path, such as <c>/digitals.bookings.v1.BookingService/GetBooking</c>.</param>
/// <param name="Kind">unary, client streaming, server streaming or bidirectional streaming.</param>
/// <param name="Request">The request message's full name, such as <c>.digitals.bookings.v1.GetBookingRequest</c>.</param>
/// <param name="Response">The response message's full name.</param>
/// <param name="Comment">The comment above the method.</param>
/// <param name="Deprecated">Whether the method is marked deprecated.</param>
internal sealed record Rpc(
    string Id,
    string Path,
    string Kind,
    string Request,
    string Response,
    string Comment,
    bool Deprecated);

/// <summary>
/// A compiled contract: its RPCs, and every message and enum by full name. buf compiles it with protovalidate
/// beside it, so a contract that imports buf/validate/validate.proto compiles without a dependency to fetch.
/// </summary>
internal sealed partial class Contract
{
    private Contract(List<Rpc> rpcs, Dictionary<string, JsonObject> messages, Dictionary<string, JsonObject> enums)
    {
        Rpcs = rpcs;
        Messages = messages;
        Enums = enums;
    }

    /// <summary>Every RPC, service by service in the files' order.</summary>
    public List<Rpc> Rpcs { get; }

    /// <summary>Every message in the contract, nested ones too, by full name with a leading dot.</summary>
    public Dictionary<string, JsonObject> Messages { get; }

    /// <summary>Every enum in the contract, by full name with a leading dot.</summary>
    public Dictionary<string, JsonObject> Enums { get; }

    /// <summary>Where a contract imports protovalidate from.</summary>
    public const string Validate = "buf/validate/validate.proto";

    /// <summary>The validate.proto the tool ships.</summary>
    public static string Bundled => Path.Combine(AppContext.BaseDirectory, "native", "validate.proto");

    /// <summary>The contract in <paramref name="directory"/>, or buf's reasons it can't compile it.</summary>
    public static (Contract? Contract, string[] Errors) Read(string directory, string buf)
    {
        var work = Directory.CreateTempSubdirectory("dt-protobuf-");
        try
        {
            var files = Directory.GetFiles(directory, "*.proto", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                return (null, ["the directory holds no .proto files"]);
            }

            foreach (var file in files)
            {
                Copy(file, Path.Combine(work.FullName, "contract", Path.GetRelativePath(directory, file)));
            }

            // A contract that brings its own copy of protovalidate keeps it; two copies wouldn't compile.
            string[] modules = ["contract"];
            if (!File.Exists(Path.Combine(work.FullName, "contract", Validate)))
            {
                Copy(Bundled, Path.Combine(work.FullName, "protovalidate", Validate));
                modules = [.. modules, "protovalidate"];
            }

            var config = string.Concat(modules.Select(m => $"  - path: {m}\n"));
            File.WriteAllText(Path.Combine(work.FullName, "buf.yaml"), $"version: v2\nmodules:\n{config}");
            var (exitCode, output, errors) = Run(buf, work.FullName,
                ["build", "contract", "--exclude-imports", "-o", "-#format=json"]);
            if (exitCode != 0)
            {
                var lines = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return (null, [.. lines.Select(e => Where(e, directory))]);
            }

            return (Parse(JsonNode.Parse(output)!.AsObject()), []);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    // buf names a file by its place in the work directory; the reader knows it by its place in theirs.
    private static string Where(string error, string directory) =>
        Workspace().Replace(error, $"{directory.TrimEnd('/', '\\')}/", 1);

    [GeneratedRegex(@"^contract[/\\]")]
    private static partial Regex Workspace();

    /// <summary>Copies a file, making the directory it goes in.</summary>
    public static void Copy(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
    }

    /// <summary>
    /// Runs buf in <paramref name="directory"/> and returns what it wrote. buf reads its config from the directory
    /// it runs in, and v2 module paths have to sit inside it.
    /// </summary>
    public static (int ExitCode, string Output, string Errors) Run(string buf, string directory, string[] args)
    {
        var start = new ProcessStartInfo(buf)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var errors = new StringBuilder();
        // The last event carries no line: the stream has ended.
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                errors.AppendLine(line.Data);
            }
        };
        process.BeginErrorReadLine();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, errors.ToString());
    }

    private static Contract Parse(JsonObject image)
    {
        var rpcs = new List<Rpc>();
        var messages = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var enums = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var file in Objects(image["file"]))
        {
            var package = (string?)file["package"] is { } name ? $".{name}" : "";
            Types(package, Objects(file["messageType"]), Objects(file["enumType"]), messages, enums);
            var comments = Objects(file["sourceCodeInfo"]!["location"])
                .Where(l => l["leadingComments"] is not null)
                .ToDictionary(
                    l => l["path"]!.ToJsonString(),
                    l => (string)l["leadingComments"]!,
                    StringComparer.Ordinal);
            var services = Objects(file["service"]).ToArray();
            for (var s = 0; s < services.Length; s++)
            {
                var methods = Objects(services[s]["method"]).ToArray();
                for (var m = 0; m < methods.Length; m++)
                {
                    var comment = comments.GetValueOrDefault($"[6,{s},2,{m}]", "");
                    rpcs.Add(Method(package, (string)services[s]["name"]!, methods[m], comment));
                }
            }
        }

        return new(rpcs, messages, enums);
    }

    // A nested type's full name is its parent's and its own.
    private static void Types(
        string scope,
        IEnumerable<JsonObject> messageTypes,
        IEnumerable<JsonObject> enumTypes,
        Dictionary<string, JsonObject> messages,
        Dictionary<string, JsonObject> enums)
    {
        foreach (var type in enumTypes)
        {
            enums[$"{scope}.{type["name"]}"] = type;
        }

        foreach (var type in messageTypes)
        {
            var name = $"{scope}.{type["name"]}";
            messages[name] = type;
            Types(name, Objects(type["nestedType"]), Objects(type["enumType"]), messages, enums);
        }
    }

    private static Rpc Method(string package, string service, JsonObject method, string comment)
    {
        var name = (string)method["name"]!;
        var kind = ((bool?)method["clientStreaming"] ?? false, (bool?)method["serverStreaming"] ?? false) switch
        {
            (false, false) => "unary",
            (true, false) => "client streaming",
            (false, true) => "server streaming",
            _ => "bidirectional streaming",
        };
        return new(
            $"{service}.{name}",
            $"/{package.TrimStart('.')}{(package.Length > 0 ? "." : "")}{service}/{name}",
            kind,
            (string)method["inputType"]!,
            (string)method["outputType"]!,
            comment,
            (bool?)method["options"]?["deprecated"] ?? false);
    }

    /// <summary>The objects in a JSON array, or none when there's no array.</summary>
    public static IEnumerable<JsonObject> Objects(JsonNode? array) =>
        array is JsonArray items ? items.OfType<JsonObject>() : [];
}
