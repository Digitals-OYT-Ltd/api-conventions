# AGENTS.md: agent instructions

> Canonical agent/project instructions for this repo. Tool-agnostic.

api-conventions, .NET 10 dotnet tools: the Digitals contract linters, one per format, each wrapping open-source linters with the Digitals rules. `dt-openapi` (vacuum and oasdiff, HTTP APIs), `dt-asyncapi` (Spectral, events), `dt-protobuf` (buf and protoc-gen-doc, gRPC).

## Tooling: `just`

- `just test`: every ruleset and command test; `just test OpenApi` (or `AsyncApi`, `Protobuf`) runs one tool's. Digitals.Testing fails the run below 100% line and branch coverage of the tool, so a new branch needs a test through the tool. Only code a macOS or Linux run can't reach gets `[ExcludeFromCodeCoverage(Justification = ...)]`, as `Executable` does.
- `just format` fixes formatting; `just format-check` is what CI runs.
- `just pack`: every `.nupkg` into `artifacts/`; `just pack OpenApi` packs one tool. Each tool packs once per platform and downloads that platform's linters the first time, about 1 GB in all.
- Release one tool by tagging `openapi/vX.Y.Z`, `asyncapi/vX.Y.Z` or `protobuf/vX.Y.Z`; its workflow in `.github/workflows/` publishes it. A file shared by several tools (`src/*.targets`, `src/ViewerPage.cs`, the props files) is in every affected workflow's `paths`, so add a new shared file there too.
- Run one tool from source: `dotnet run --project src/Digitals.OpenApi -- docs examples/openapi.yaml`.
- Upgrade buf, Spectral, protoc-gen-doc or protovalidate: change `BufVersion`, `SpectralVersion`, `ProtocGenDocVersion` or `ProtovalidateVersion` in the csproj and every `Sha256` (buf's `sha256.txt`, the GitHub asset digests for Spectral, protoc-gen-doc's `checksums.txt`, `shasum -a 256` of protovalidate's two raw files). protoc-gen-doc ships in a `.tar.gz`, so its `_NativeDownload` items name the file with `Extract` and their `Sha256` is the archive's.
- `.github/dependabot.yml` updates NuGet packages, Actions and the SDK; it can't see the bundled tools. `.github/workflows/security.yml` scans those weekly and opens an issue on a known vulnerability. It reads versions from the csprojs, so a new bundled tool needs a line there and a bump doesn't. `SECURITY.md` is the runbook for withdrawing a vulnerable release, with `.github/workflows/unlist.yml`.
- Upgrade Swagger UI or the AsyncAPI viewer: change `SwaggerUiVersion` or `AsyncApiReactVersion` and every `_WebAsset` hash (`shasum -a 256` of each file), then open a page.

Verification gate: before claiming done, run `just format-check` and `just test`. After changing a rule, also run `just test-template` in `../csharp-api-template`, which packs this repo; the template's sample contract must stay clean.

## Standards: mandatory consult

`dt rules csharp` before C# work, `dt rules testing` before tests. Cite rule IDs such as `design/require-pagination`.

## Layout

- `src/Digitals.<Format>/` is one tool, tested in `tests/Digitals.<Format>.Tests/`. `src/Digitals.OpenApi/AGENTS.md` covers the OpenAPI rules and vacuum; `src/Digitals.AsyncApi/AGENTS.md` covers the AsyncAPI rules and Spectral; `src/Digitals.Protobuf/AGENTS.md` covers the protobuf rules, buf and the docs page.
- `src/` root holds what more than one tool uses: `ViewerPage.cs` (compiled into all three), `NativeBinaries.targets` (binaries for AsyncApi and Protobuf), `WebAssets.targets` (viewer files for OpenApi and AsyncApi), and the `cases` code: `ContractYaml.cs` (reads the file) and `SchemaRules.cs` (the rule cases for a schema, field by field) in OpenApi and AsyncApi, and in all three `CaseList.cs` (prints them), `Sentences.cs` (counts and lists in a detail) and `VersionCheck.cs` (warns when nuget.org lists the running version as vulnerable or deprecated). `tests/VersionCheckTests.cs` is compiled into all three test projects, so each covers `VersionCheck.cs` itself. Each tool's tests must cover the shared files 100% on their own.
- `examples/` holds one full contract per format, which the tests lint clean, and the page each tool writes from it. After editing a contract, regenerate its `.html` with `docs`; `dt-protobuf docs` needs the output path (`examples/protobuf.html`), or the page lands in the working directory. After editing a contract, also regenerate its cases: `dotnet run --project src/Digitals.OpenApi -- cases examples/openapi.yaml > examples/openapi.cases.txt`, the same with `AsyncApi` and `asyncapi`, and `dotnet run --project src/Digitals.Protobuf -- cases examples/protobuf > examples/protobuf.cases.txt`.
- A viewer page is `viewer/<template>.html` in the tool's project, with the script, styles and contract inlined by `ViewerPage.Write`.

## Gotchas

- Run one test project with `dotnet test --project <path>`. `dotnet test <path>` reports "Zero tests ran".
- Tests run the tool from the test project's bin folder as a child process, so a default output lands there. The runners (`Vacuum.cs`, `Spectral.cs`, `Buf.cs`) set `DT_VERSION_CHECK=off` so a test never waits on nuget.org; a new runner must too.
- A viewer page must open from a file with no network. Never add a `<script src>` or `<link>`; the tests check.
- The AsyncAPI function's `check()` is a copy of the OpenAPI validator's (`src/Digitals.OpenApi/functions/exampleMatchesSchema.js`), so change both together.
- A file in `obj/native/<rid>/` or `obj/web/` is cached under its hash, so a wrong hash fails the build instead of reusing an old file. Native files sit under their RID because pack builds every RID in parallel.
- Silence an analyzer rule for one file in `.editorconfig`, with a comment giving the reason, as RCS0056 is for `RulesetTests.cs`.
- Microsoft.OpenApi 3.11, which `cases` reads schemas with in both tools:
  - SharpYaml expands an alias but keeps a merge key as a literal `<<` key, so `ContractYaml.Merge` folds merge keys in before the reader sees them. It also reads a YAML `null` as a sentinel string that `JsonNullSentinel.IsJsonNullSentinel` finds, in an enum or anywhere else.
  - It reports a bad contract in `Diagnostic.Errors`, except an `openapi` version it doesn't know (`OpenApiUnsupportedSpecVersionException`, not an `OpenApiException`) and a number keyword with something else in it (`FormatException`), which throw.
  - A schema's `example` lands only in the obsolete `Example`.
- Coverage counts `$"{x}"` on a `string?` as a branch (`x ?? ""`), and `?.` and `??` too. For a value the parser requires but types as nullable, use `OrEmpty`, `OfType` or `AsSpan`, which no contract can send down an untested branch.

## Domain notes

- Each tool has two rule levels. The baseline is the industry basics, for auditing an existing API with `--baseline`; the house rules extend it, and a new contract must pass them.
- The rules encode `docs/api-conventions.md` in csharp-api-template, which pins `dt-openapi` in the repos it generates.
- A command returns the wrapped tool's exit code, so CI fails when it fails. The tool's own usage errors exit 2, and no arguments exits 1.

## Recorded deviations

None.
