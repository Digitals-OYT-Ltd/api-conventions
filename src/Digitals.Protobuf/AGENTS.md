# AGENTS.md: dt-protobuf

The protobuf rules, buf, and the docs page from protoc-gen-doc.

## Layout

- A rule is a buf category or rule ID in `buf.yaml` (house) or `buf-baseline.yaml` (baseline): add it under `lint.use`, or remove one with `lint.except` and the reason.
- Test a rule in `tests/Digitals.Protobuf.Tests/RulesTests.cs`. It writes the `.proto` into a module laid out by package (`acme/notes/v1/notes.proto`) and reads buf's JSON findings by `type`.
- `viewer/protobuf.js` is our own renderer: buf compiles the input, protoc-gen-doc turns it into JSON, and the script draws the page from that.

## Gotchas

- `dt-protobuf` passes our config to buf with `--config`, so a project's own `buf.yaml` is ignored.
- buf exits 100 on a finding, not 1. It names the rule only in `--error-format json`; the text output says what to change.
- `examples/protobuf` must stay clean under both `lint` and `format`; `Lint_FullExample_FindsNothing` checks.
- `protobuf.js` puts contract text in through text nodes only, since a `.proto` comment is free text. Keep it that way.
- protoc-gen-doc's JSON has no field numbers and no oneof comments, so the page can't show them. It documents only the input's own files, not imports such as `google/protobuf/timestamp.proto`.
