# AGENTS.md: agent instructions

> Canonical agent/project instructions for this repo. Tool-agnostic.

api-conventions, .NET 10 dotnet tools: the Digitals contract linters, one per format, each wrapping open-source linters with the Digitals rules. `dt-openapi` (vacuum and oasdiff, HTTP APIs), `dt-asyncapi` (Spectral, events), `dt-protobuf` (buf and protoc-gen-doc, gRPC).

## Tooling: `just`

- `just test`: every ruleset and command test; `just test OpenApi` (or `AsyncApi`, `Protobuf`) runs one tool's. Digitals.Testing fails the run below 100% line and branch coverage of the tool, so a new branch needs a test through the tool. Only code a macOS or Linux run can't reach gets `[ExcludeFromCodeCoverage(Justification = ...)]`, as `Executable` does.
- `just format` fixes formatting; `just format-check` is what CI runs.
- `just pack`: every `.nupkg` into `artifacts/`; `just pack OpenApi` packs one tool. Each tool packs once per platform and downloads that platform's linters the first time, about 1 GB in all.
- Release one tool by tagging `openapi/vX.Y.Z`, `asyncapi/vX.Y.Z` or `protobuf/vX.Y.Z`; its workflow in `.github/workflows/` publishes it. A file shared by several tools (`src/*.targets`, `src/ViewerPage.cs`, the props files) is in every affected workflow's `paths`, so add a new shared file there too.
- Run one tool from source: `dotnet run --project src/Digitals.OpenApi -- swagger examples/openapi.yaml`.
- Upgrade buf, Spectral or protoc-gen-doc: change `BufVersion`, `SpectralVersion` or `ProtocGenDocVersion` in the csproj and every `Sha256` (buf's `sha256.txt`, the GitHub asset digests for Spectral, protoc-gen-doc's `checksums.txt`). protoc-gen-doc ships in a `.tar.gz`, so its `_NativeDownload` items name the file with `Extract` and their `Sha256` is the archive's.
- Upgrade Swagger UI or the AsyncAPI viewer: change `SwaggerUiVersion` or `AsyncApiReactVersion` and every `_WebAsset` hash (`shasum -a 256` of each file), then open a page.

Verification gate: before claiming done, run `just format-check` and `just test`. After changing a rule, also run `just test-template` in `../csharp-api-template`, which packs this repo; the template's sample contract must stay clean.

## Standards: mandatory consult

`dt rules csharp` before C# work, `dt rules testing` before tests. Cite rule IDs such as `design/require-pagination`.

## Layout

- `src/Digitals.<Format>/` is one tool, tested in `tests/Digitals.<Format>.Tests/`. `src/Digitals.OpenApi/AGENTS.md` covers the OpenAPI rules and vacuum; `src/Digitals.AsyncApi/AGENTS.md` covers the AsyncAPI rules and Spectral; `src/Digitals.Protobuf/AGENTS.md` covers the protobuf rules, buf and the docs page.
- `src/` root holds what more than one tool uses: `ViewerPage.cs` (compiled into all three), `NativeBinaries.targets` (binaries for AsyncApi and Protobuf), `WebAssets.targets` (viewer files for OpenApi and AsyncApi).
- `examples/` holds one full contract per format, which the tests lint clean, and the page each tool writes from it. After editing a contract, regenerate its `.html` with `swagger` or `docs`; `dt-protobuf docs` needs the output path (`examples/protobuf.html`), or the page lands in the working directory.
- A viewer page is `viewer/<template>.html` in the tool's project, with the script, styles and contract inlined by `ViewerPage.Write`.

## Gotchas

- Run one test project with `dotnet test --project <path>`. `dotnet test <path>` reports "Zero tests ran".
- Tests run the tool from the test project's bin folder as a child process, so a default output lands there.
- A viewer page must open from a file with no network. Never add a `<script src>` or `<link>`; the tests check.
- The AsyncAPI function's `check()` is a copy of the OpenAPI validator's (`src/Digitals.OpenApi/functions/exampleMatchesSchema.js`), so change both together.
- A file in `obj/native/<rid>/` or `obj/web/` is cached under its hash, so a wrong hash fails the build instead of reusing an old file. Native files sit under their RID because pack builds every RID in parallel.
- Silence an analyzer rule for one file in `.editorconfig`, with a comment giving the reason, as RCS0056 is for `RulesetTests.cs`.

## Domain notes

- Each tool has two rule levels. The baseline is the industry basics, for auditing an existing API with `--baseline`; the house rules extend it, and a new contract must pass them.
- The rules encode `docs/api-conventions.md` in csharp-api-template, which pins `dt-openapi` in the repos it generates.
- A command returns the wrapped tool's exit code, so CI fails when it fails. The tool's own usage errors exit 2, and no arguments exits 1.

## Recorded deviations

None.
