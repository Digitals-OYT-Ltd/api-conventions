# AGENTS.md: dt-protobuf

The protobuf rules, buf, and the docs page from protoc-gen-doc.

## Layout

- A rule is a buf category or rule ID in `buf.yaml` (house) or `buf-baseline.yaml` (baseline): add it under `lint.use`, or remove one with `lint.except` and the reason.
- Test a rule in `tests/Digitals.Protobuf.Tests/RulesTests.cs`. It writes the `.proto` into a module laid out by package (`acme/notes/v1/notes.proto`) and reads buf's JSON findings by `type`.
- `cases` is `Contract.cs` (compiles the directory and reads buf's JSON image), `Protovalidate.cs` (the rule cases for a request, field by field) and `TestCases.cs`. A protovalidate rule is an entry in `TextRules`, `BoundRules`, `Enum` or `Listed`; one without an entry still gets a case that names it. Test it in `CasesTests.cs`, whose contract uses every rule kind.
- `viewer/protobuf.js` is our own renderer: buf compiles the input, protoc-gen-doc turns it into JSON, and the script draws the page from that.

## Gotchas

- `dt-protobuf` passes our config to buf with `--config`, so a project's own `buf.yaml` is ignored.
- buf exits 100 on a finding, not 1. It names the rule only in `--error-format json`; the text output says what to change.
- `examples/protobuf` must stay clean under both `lint` and `format`; `Lint_FullExample_FindsNothing` checks.
- `protobuf.js` puts contract text in through text nodes only, since a `.proto` comment is free text. Keep it that way.
- `cases` copies the input's `.proto` files into a temp v2 workspace beside the bundled `validate.proto` (`native/`) and runs `buf build` there. buf resolves v2 module paths against its working directory and refuses paths outside it, so it must run with that directory as cwd. buf's errors name `contract/...`, which `Contract.Where` maps back to the input.
- `lint`, `breaking` and `docs` run on a copy (`Staging.cs`) when a directory imports `buf/validate/validate.proto` without its own copy: the bundled file goes inside it, and `Staging.Run` captures buf's output to turn the copy's path back into the input's. `lint` and `generate` take `--exclude-path` for the bundled file; `breaking` crashes on it ("is outside the context directory"), so it gets none and finds the same file on both sides. A base that isn't a directory, such as a git ref, is written out with `buf format -o`, which doesn't compile. `format` only parses, so it needs no copy, and `--write` must reach the real files.
- buf's JSON image writes 64-bit numbers, durations and timestamps as strings, rule names in camelCase (`maxLen`), and bytes in base64. A predefined rule is a key in brackets, `[acme.notes.v1.even]`, and can only be declared in a proto2 or editions file.
- protoc-gen-doc's JSON has no field numbers and no oneof comments, so the page can't show them. It documents only the input's own files, not imports such as `google/protobuf/timestamp.proto`.
