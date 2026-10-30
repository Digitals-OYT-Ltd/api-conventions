# Digitals.OpenApi

`dt-openapi` lints an OpenAPI 3.0 or 3.1 document against the Digitals API rules and compares two documents. It bundles [vacuum](https://github.com/daveshanley/vacuum) and [oasdiff](https://github.com/oasdiff/oasdiff), so nothing else needs installing.

```sh
dotnet tool install Digitals.OpenApi        # or add it to .config/dotnet-tools.json
dt-openapi lint contract/openapi.yaml       # exit 1 on any finding
dt-openapi diff contract/openapi.yaml src/Acme.Api/openapi.json
dt-openapi swagger contract/openapi.yaml    # writes contract/openapi.html
```

| Command | Does |
| --- | --- |
| `lint <spec>` | Fails when the document breaks a rule, and lists every finding with its file, line and JSON path. Any path or URL, so it audits existing APIs too. A spec outside the current directory is shown by its absolute path. |
| `report <spec> [<out.html>]` | Writes an HTML report you can open and share. |
| `dashboard <spec>` | Browses the findings in the terminal. |
| `swagger <spec> [<out.html>]` | Writes the contract as a Swagger UI page, by default beside the spec (`contract/openapi.html`). Swagger UI and the contract are inlined, so the page opens from the file with no network and can be sent as an attachment. A contract split across files needs bundling first (`dt-openapi vacuum bundle`). |
| `diff <design> <implementation>` | Fails when the implementation differs from the design. Wording, examples, titles, `x-dt-unconditional-write` and `x-dt-no-idempotency-key` may differ. |
| `breaking <base> <revision>` | Fails on a breaking change between two versions of a contract. Removing an operation needs it deprecated first with an `x-sunset` date that has passed, and a new sunset must be at least 180 days out; `--deprecation-days-stable <days>` changes that. A new value in a response enum is not breaking, since clients tolerate values they don't know. |
| `changelog <base> <revision>` | Lists the changes. Add `--format html` for a page. |
| `ruleset [<out.yaml>]` | Prints the ruleset. With a path it writes `digitals.yaml`, the baseline it extends and the `functions` folder its JavaScript rules need, side by side. |
| `vacuum <args>`, `oasdiff <args>` | Runs the bundled tool directly. |

[examples/openapi.yaml](../../examples/openapi.yaml) is a full HTTP API that passes every rule: page paging with `totalCount`, idempotent creates, `ETag` and `If-Match`, a merge patch, `:cancel` and `:search` custom methods, a multipart upload, a file download, a deprecated operation, OAuth scopes, links, a `oneOf` with a discriminator and a webhook. Copy from it, or open [examples/openapi.html](../../examples/openapi.html), the page `dt-openapi swagger examples/openapi.yaml` writes from it.

Options after the arguments go to the underlying tool, for example `dt-openapi lint api.yaml --github-annotations`. `lint`, `report`, `dashboard` and `ruleset` take `--baseline` for the baseline ruleset. `lint`, `report` and `dashboard` take `--ruleset <file>` to use your own ruleset instead.

The conventions behind the rules are in `docs/api-conventions.md` in csharp-api-template.

## Rules

There are two rulesets, and any rule left on fails `lint`:

- `digitals-baseline.yaml`, used with `--baseline`, is the industry basics every HTTP API must meet whatever its design: a valid document, the OWASP API Security Top 10 rules, documented operations, valid examples and no personal data in a URL. Audit an existing API with it.
- `digitals.yaml`, the default, extends the baseline with the Digitals house rules for resources, naming, paging, concurrency, errors and documentation. A new contract must pass it.

The [OpenAPI rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/openapi.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

The baseline lists every vacuum built-in, each set to `error` or turned `off` with the reason. Besides the OpenAPI validity rules (`oas3-schema`, `path-params`, `no-ambiguous-paths`, `oas3-unused-component` and the rest) it enforces:

| Rule | Fires on | Standard |
| --- | --- | --- |
| `operation-operationId`, `operation-operationId-unique` | An operation with no `operationId`, or two with the same one | `design/require-complete-openapi` |
| `operation-tags`, `operation-tag-defined`, `openapi-tags` | An operation with no tag, or a tag missing from the top-level `tags` | `design/require-complete-openapi` |
| `oas3-parameter-description`, `operation-description` | A parameter or operation with no description. An operation's summary counts | `design/require-complete-openapi` |
| `info-description` | A document with no description | `design/require-complete-openapi` |
| `oas3-valid-schema-example` | A parameter or header example that doesn't match its schema, or a component schema example of the wrong type or outside its enum | |
| `paths-specificity-order` | A templated path listed before a fixed path it could also match: `/notes/{noteId}` above `/notes/archived`. Some routers and mock servers take the first match | |
| `owasp-protection-global-unsafe`, `-strict`, `owasp-protection-global-safe` | An operation with no `security`, globally or on the operation | OWASP API2:2023 |
| `owasp-define-error-responses-401`, `owasp-define-error-responses-500` | An operation with no 401 or no 500 response | OWASP API8:2023 |
| `owasp-rate-limit` | A 2xx or 4xx response without rate-limit headers. Declare `RateLimit-Policy` and `RateLimit` (draft-ietf-httpapi-ratelimit-headers) | OWASP API4:2023 |
| `owasp-define-error-responses-429`, `owasp-rate-limit-retry-after` | An operation with no 429 response, or a 429 without a body schema or a `Retry-After` header. The second only checks a 429 that declares some header | OWASP API4:2023 |
| `owasp-no-api-keys-in-url`, `owasp-no-credentials-in-url` | A credential in a path or query parameter. Any parameter whose name contains `token` counts | OWASP API2:2023 |
| `owasp-no-http-basic`, `owasp-auth-insecure-schemes`, `owasp-oauth-no-implicit`, `owasp-oauth-no-password` | HTTP Basic, or an insecure scheme or OAuth flow | OWASP API2:2023 |
| `owasp-no-numeric-ids` | An integer ID parameter. Use a UUID | OWASP API1:2023 |
| `owasp-string-restricted` | A string with no `format`, `pattern`, `enum` or `const` | OWASP API4:2023 |
| `owasp-integer-limit` | An integer without both a minimum and a maximum, inclusive or exclusive | OWASP API4:2023 |
| `owasp-integer-format` | An integer with no `format` | OWASP API4:2023 |
| `owasp-no-additionalProperties` | A request schema without `additionalProperties: false` or a bounded map | OWASP API3:2023 |
| `owasp-constrained-additionalProperties` | A map-shaped request schema with no `maxProperties` | OWASP API3:2023 |
| `owasp-string-limit`, `owasp-array-limit` | A request string with no `maxLength`, `enum` or `const`, or a request array with no `maxItems` | OWASP API4:2023 |
| `dt-no-personal-data-in-url` | A path or query parameter that names personal data: a national ID (NHS or NI number), date of birth, postcode, address, email, phone or name. URLs end up in proxy and server logs. Send it in the body of a `POST .../:search` | `docs/standards/design/security-and-data-protection.md#require-tls-and-encryption` |
| `dt-operation-summary` | An operation with no summary | `design/require-complete-openapi` |
| `dt-example-valid` | A request, response or component schema example that doesn't match its schema: `example` and every named example in `examples`. It checks type, `enum`, `const`, `required`, `additionalProperties: false`, lengths, `pattern`, ranges, item counts, `allOf`, `anyOf`, `oneOf` and the `uuid`, `date`, `date-time`, `time`, `email` and `uri` formats. vacuum's own check covers parameters and headers but skips bodies. The rule is JavaScript in `functions/` | |

`digitals.yaml` adds these built-ins:

| Rule | Fires on | Standard |
| --- | --- | --- |
| `operation-singular-tag` | An operation with more than one tag | `design/require-complete-openapi` |
| `tag-description` | A tag with no description | `design/require-complete-openapi` |
| `no-http-verbs-in-path` | A path segment that is an HTTP method: `/notes/get`, `/notes/{noteId}/delete` | |

And the Digitals rules. `dt-string-limit`, `dt-array-limit`, `dt-description` and `dt-operation-id-casing` are stricter versions of baseline built-ins, which `digitals.yaml` turns off:

| Rule | Fires on | Standard |
| --- | --- | --- |
| `dt-path-version` | A path without exactly one version segment `v{n}`, where `n` is a whole number from 1: `v1`, never `v1.0` or `v01`. Segments before it are the app's choice | `design/use-url-path-versioning` |
| `dt-path-segments` | A path segment that isn't lowercase kebab-case, a `{parameter}`, or either one with a `:custom-method` suffix | `design/use-resource-paths`, `design/use-colon-custom-methods` |
| `dt-path-id` | A path parameter that isn't camelCase ending in `Id`: `{submissionId}` passes, `{id}` and `{submission}` fail. The rule can't check that the prefix is the resource's singular name | |
| `dt-path-depth` | A path with more than three `{parameter}` segments. Nest by ownership only; a deeper child becomes a top-level resource filtered by query | |
| `dt-no-http-verb-action` | A custom method named after an HTTP method: `:get`, `:delete`, `:patch`. Use the HTTP method on the resource. `:batch-get` passes | |
| `dt-path-word-count` | A resource name or custom method verb of more than two words: `bookings` and `:batch-get` pass, `venue-booking-requests` fails | |
| `dt-parameter-casing` | A path or query parameter that isn't camelCase | `design/use-consistent-json-payloads` |
| `dt-description` | An operation, request body, header, schema, property, security scheme or server with no description. A summary doesn't count; a property that is only a `$ref` takes the component's | `design/require-complete-openapi` |
| `dt-example` | A component schema, parameter, header or request or response body with no example. A body whose schema is a `$ref` takes the component's. Replaces `oas3-missing-example`, which flags the first schema's first property whatever it has and skips parameters | |
| `dt-operation-id-casing` | An `operationId` that isn't PascalCase letters and digits, or ends in `Async`: `CreateNote`, not `createNote`, `Notes_Create` or `CreateNoteAsync` | |
| `dt-problem-json` | A 4xx or 5xx response whose body isn't exactly `application/problem+json`, or that has no body | `design/use-problem-details-errors` |
| `dt-error-example` | A 4xx or 5xx body with no example of its own, or one whose `status` isn't the response code. `dt-example` lets a body pass on its schema's example, which for an error belongs to another status, so a 401 would show a 404. JavaScript in `functions/` | `design/use-problem-details-errors` |
| `dt-no-bare-array-response` | A 2xx response whose body is an array, inline or through a `$ref` | `design/require-pagination` |
| `dt-string-limit` | A string with no `maxLength`, `enum` or `const`, in a request, a response, a parameter or a header. Replaces `owasp-string-limit`, which checks requests only | OWASP API4:2023 |
| `dt-array-limit` | An array with no `maxItems`, in either direction. Replaces `owasp-array-limit` | OWASP API4:2023 |
| `dt-idempotent-create` | A POST on a collection (not a `:custom-method`) without a required `Idempotency-Key` header parameter and 400 and 422 responses. A create that is safe to repeat without one sets `x-dt-no-idempotency-key` to the reason instead | draft-ietf-httpapi-idempotency-key-header |
| `dt-conditional-write` | A PUT, PATCH or DELETE without a required `If-Match` header parameter, a 412 response and a 428 response. An operation that may overwrite without a check sets `x-dt-unconditional-write` to the reason instead | RFC 9110 |
| `dt-etag-response` | A GET on a path with a PUT, PATCH or DELETE that has no `ETag` header on its 200. Writes that opt out don't count | RFC 9110 |
| `dt-property-casing` | A schema property that isn't camelCase | `design/use-consistent-json-payloads` |
| `dt-no-integer-enum` | A named component schema that is a bare integer, which is how an enum sent as its ordinal looks | `design/use-consistent-json-payloads` |
| `dt-enum-casing` | An enum value that isn't camelCase: `onHold` | |
| `dt-request-media-type` | A request body other than `application/json`, `application/merge-patch+json`, `multipart/form-data` or `application/octet-stream` | `design/use-consistent-json-payloads` |
| `dt-paging-in-query` | A paging field in a request body, `:search` included: `pageSize`, `pageCursor`, `page`, `skip`, `top`. It belongs in the query string | |
| `dt-timestamp-name`, `dt-date-name`, `dt-time-suffix-format`, `dt-date-suffix-format` | A timestamp (`format: date-time`) not named `...Time`, a date (`format: date`) not named `...Date`, or a `...Time` or `...Date` name with another format: `createTime`, `birthDate` | |
| `dt-unit-integer` | A property named `...Milliseconds`, `...Seconds`, `...Minutes`, `...Hours`, `...Days`, `...Months`, `...Years` or `...Bytes` that isn't an integer | |
| `dt-unit-name` | A property that hides or abbreviates its unit: `ttl`, `timeoutMillis`, `cycleWeeks`, `retryDelay` | |
| `dt-no-duration-string` | A .NET `TimeSpan` string (the pattern ASP.NET writes for one) or `format: duration`. Send an integer named by its unit instead | |

To skip a rule for one document, write your own ruleset that extends `digitals.yaml` or the baseline and pass it with `--ruleset`. `dt-openapi` passes the bundled `functions` folder along. List `spectral:owasp` again: vacuum doesn't follow it through a file it extends, and would drop every OWASP rule. Running vacuum yourself on that ruleset needs `--functions ./functions`.

```yaml
extends:
  - ./digitals.yaml          # from `dt-openapi ruleset digitals.yaml`
  - [spectral:owasp, all]
rules:
  owasp-no-credentials-in-url: off   # why
```

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64). vacuum is MIT licensed and oasdiff Apache-2.0; their licences ship beside the binaries in `native/`.

Swagger UI is Apache-2.0 licensed; its licence ships in `viewer/`.
