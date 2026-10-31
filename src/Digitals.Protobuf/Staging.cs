namespace Digitals.Protobuf;

/// <summary>
/// Copies of the inputs with the bundled validate.proto beside them, so lint, breaking and docs compile a
/// contract that imports protovalidate without bringing it, as cases does. buf names a file by its place in the
/// copy, so what it prints turns back into the input's path.
/// </summary>
internal sealed class Staging(string buf) : IDisposable
{
    private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("dt-protobuf-");
    private readonly List<(string Copy, string Input)> _copies = [];
    private readonly List<string> _bundled = [];

    /// <summary>Whether <paramref name="input"/> is a directory that imports protovalidate without bringing it.</summary>
    public static bool Needs(string input) =>
        Directory.Exists(input)
        && !File.Exists(Path.Combine(input, Contract.Validate))
        && Directory.EnumerateFiles(input, "*.proto", SearchOption.AllDirectories)
            .Any(file => File.ReadAllText(file).Contains(Contract.Validate, StringComparison.Ordinal));

    /// <summary>A copy of <paramref name="input"/> with protovalidate in it, or the input when buf can't read it.</summary>
    public string Add(string input)
    {
        var copy = Path.Combine(_work.FullName, $"{_copies.Count}");
        if (Directory.Exists(input))
        {
            foreach (var file in Directory.GetFiles(input, "*.proto", SearchOption.AllDirectories))
            {
                Contract.Copy(file, Path.Combine(copy, Path.GetRelativePath(input, file)));
            }

            _copies.Add((copy, $"{input.TrimEnd('/', '\\')}/"));
        }
        else
        {
            // buf format writes out any input it reads, such as a git ref, without compiling it; compiling would
            // need protovalidate. Its files then print by their path in the module, as buf prints a git ref's.
            if (Contract.Run(buf, Environment.CurrentDirectory, ["format", input, "-o", copy]).ExitCode != 0)
            {
                return input;
            }

            _copies.Add((copy, ""));
        }

        var validate = Path.Combine(copy, Contract.Validate);
        if (!File.Exists(validate))
        {
            Contract.Copy(Contract.Bundled, validate);
            _bundled.Add(validate);
        }

        return copy;
    }

    /// <summary>
    /// The options with a path in an input moved to the copy, and, when <paramref name="exclude"/> is set, the
    /// bundled files left out so buf neither lints nor documents them. breaking can't take that: it finds the
    /// same file on both sides, so it reports nothing there.
    /// </summary>
    public string[] Options(string[] options, bool exclude) =>
    [
        .. options.Select(option => _copies.Aggregate(option, (value, c) =>
            c.Input.Length > 0 && value.StartsWith(c.Input, StringComparison.Ordinal)
                ? $"{c.Copy}/{value[c.Input.Length..]}"
                : value)),
        .. exclude ? _bundled.SelectMany(file => (string[])["--exclude-path", file]) : [],
    ];

    /// <summary>Runs buf and prints what it wrote, with each copy's path turned back into its input's.</summary>
    public int Run(string[] args)
    {
        var (exitCode, output, errors) = Contract.Run(buf, Environment.CurrentDirectory, args);
        Console.Out.Write(Restore(output));
        Console.Error.Write(Restore(errors));
        return exitCode;
    }

    private string Restore(string text) => _copies.Aggregate(text, (restored, c) => restored
        .Replace($"{c.Copy}/", c.Input, StringComparison.Ordinal)
        .Replace($"{c.Copy}\\", c.Input, StringComparison.Ordinal));

    public void Dispose() => _work.Delete(recursive: true);
}
