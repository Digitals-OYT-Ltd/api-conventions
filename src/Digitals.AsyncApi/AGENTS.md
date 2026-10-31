# AGENTS.md: dt-asyncapi

The AsyncAPI rules and Spectral.

## Layout

- An industry basic goes in `digitals-baseline.yaml`, a house rule in `digitals.yaml`, which extends the baseline.
- Spectral's built-ins come from `[spectral:asyncapi, all]`. Turn one off with the reason rather than lowering it, because `dt-asyncapi lint` fails on every severity.
- A function in `functions/` is an ES module whose default export takes `(input, options, context)` and returns `[{ message, path }]`; list it under `functions:` in the ruleset.
- Test a rule in `tests/Digitals.AsyncApi.Tests/RulesetTests.cs` by editing the `Clean` document.
- `cases` reads the document as JSON in `Contract.cs`: it follows local `$ref`s, applies message traits as JSON Merge Patch, then reads each message's headers and payload with Microsoft.OpenApi from a made-up OpenAPI 3.1 document whose `components.schemas` holds the contract's own and one per message (`CreateProject.payload`). `TestCases.cs` builds the cases. A new kind of case gets a row in `tests/Digitals.AsyncApi.Tests/CasesTests.cs` against its `Contract`, and its phrase in the README's Test cases section.

## Gotchas

- Spectral 6.17:
  - Its AsyncAPI 3 rules are a separate set (`asyncapi-3-*`), and several v2 checks have no v3 twin, such as `asyncapi-message-examples` (`dt-message-example-valid` covers it with `formats: [aas3]`). Check that a built-in fires on a 3.x document before relying on it.
  - Overriding a rule name Spectral doesn't know is an error. Check the name against its AsyncAPI rules reference.
  - A filter on a field (`[?(@.format == 'date')]`) crashes the run on a null node with `Cannot read properties of null`. Guard every filter with `@ &&`.
  - The binary prints a Node `punycode` deprecation warning to stderr on every run, so read reports from stdout.

- AsyncAPI's `discriminator` is a property name and OpenAPI's an object, so `Contract.Clean` drops a string one before Microsoft.OpenApi reads the schema.

## Domain notes

- There is no AsyncAPI breaking-change check: no AsyncAPI diff tool ships a standalone binary.
