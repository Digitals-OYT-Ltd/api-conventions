# AGENTS.md: dt-openapi

The OpenAPI rules, vacuum and oasdiff, and the Swagger UI page.

## Tooling

- Upgrade vacuum or oasdiff: change `VacuumVersion` or `OasdiffVersion` and every hash in `NativeTools.targets` (from the release's checksums), then run `just test` here and `just test-template` in csharp-api-template.

## Layout

- A rule about an HTTP contract is a `dt-` rule. One every HTTP API should meet whatever its design (validity, security, documentation) goes in `digitals-baseline.yaml`; a house rule goes in `digitals.yaml`, which extends the baseline.
- Prefer a vacuum built-in when one exists, and turn it on with `error`.
- A check JSONPath and `function: schema` can't express is a JavaScript function in `functions/`: `getSchema()` returns its name, `runRule(input)` returns `[{ message }]`, and the input arrives with `$ref` resolved.
- `cases` reads the contract with Microsoft.OpenApi (`Contract.cs`) and builds the response and parameter cases in `TestCases.cs`; the field rules come from the shared `src/SchemaRules.cs`. A new kind of case gets a row in `tests/Digitals.OpenApi.Tests/CasesTests.cs` against its `Contract`, and the phrase it prints in the README's Test cases section if it changes what a reader expects.
- Test a rule in `tests/Digitals.OpenApi.Tests/RulesetTests.cs` by editing the `Clean` document, add a `LintBaseline_` case for its level, and add its section to `docs/rules/openapi.md` in the shape the others have: badges, why, a red and green diff, the real message. `RulesPageTests` fails until the page and the rulesets agree.

## Gotchas

- vacuum stays on 0.35.2: 0.35.4 to 0.36.1 lose YAML anchors inside rule functions (`unknown anchor 'rateLimit' referenced`) and fail the example with about 130 false findings. Try a newer release by swapping it into `tests/Digitals.OpenApi.Tests/bin/Debug/net10.0/native/vacuum` and running `dotnet test --project tests/Digitals.OpenApi.Tests --no-build`.
- vacuum, found on 0.33 and worked around the same on 0.35.2:
  - A `$..[?(...)]` filter, or a path starting `$..` at the root, reports the wrong node. Start at `$.components.schemas` or `$.paths`, select keys with `~` (`$..properties[*]~`), or filter a direct child (`[?(@.format == 'date')]`, `$.paths[?(@property.match(/^[^:]*$/))]`).
  - A filter can't use `=~`. To check a field by its name, filter on the name and validate the node: `$..properties[?(@property.match(/Date$/))]` with `function: schema`, no `field`, and `{{property}}` in the message, as `dt-date-suffix-format` does. Adding `field:` breaks the reported path, and a filter on a value (`[?(@.format != 'date')]~`) can't see through an anchor or merge.
  - Write a `given` list as a block list: in `[$..x[*]]` the `*]` reads as an alias.
  - `function: schema` without a `field` checks nothing unless `forceValidationOnCurrentNode: true` is set.
  - `rule: error` only changes a rule that `extends` already turned on, so the ruleset extends `all` and lists every built-in.
  - A rule's `given` sees a body's `schema: { $ref }` already resolved, so check the target's fields, not `$ref`.
  - A bare YAML alias (`*name`) hides its node's fields from JSONPath rules such as `dt-description`. Inside a schema, reuse a node with `{ <<: *name }`. Anywhere else a merge key is read as a literal `<<` key, so use a bare alias there; `owasp-rate-limit` and the other built-ins see through one.
  - A rule past `--timeout` (default 5 seconds) silently loses its findings and the run passes. `lint`, `report` and `dashboard` pass `--timeout 300`; keep it on any new command that lints.
  - A JavaScript function gets no options (`context` is unusable), so each behaviour is its own file. A result's `path` changes the printed JSON path but not the line. The functions log to stdout on every run, and only `--silent` hides that.
- Built-ins replaced by `dt-` rules:
  - `owasp-string-limit` and `owasp-array-limit` only check request schemas; `dt-string-limit` and `dt-array-limit` replace them.
  - `oas3-missing-example` flags the first component schema's first property whatever it has and skips parameters; `dt-example` replaces it.
  - `oas3-valid-schema-example` checks parameter and header examples, and a component schema example's type and enum only. It skips bodies, formats and undefined fields; `dt-example-valid` covers them.
- Microsoft.OpenApi reads 3.0 as 3.1: `nullable` becomes a `Null` type flag, and a boolean `exclusiveMinimum` a number.
- Swagger UI resolves an OpenAPI 3.1 contract's `$ref`s by fetching the contract's http(s) URL again each time an operation opens, which a `file://` page (origin `null`, so no `blob:` URL either) can't do. `viewer/swagger.html` therefore names the contract `https://contract.invalid/openapi` and wraps `fetch` to answer that URL from the inlined text. A headless check of the page must open an operation (`openapi.html#/Bookings/CreateBooking`) to see resolver errors.
