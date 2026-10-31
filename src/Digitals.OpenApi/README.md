# Digitals.OpenApi

`dt-openapi` lints an OpenAPI 3.0 or 3.1 document against the Digitals API rules and compares two documents. It bundles [vacuum](https://github.com/daveshanley/vacuum) and [oasdiff](https://github.com/oasdiff/oasdiff), so nothing else needs installing.

```sh
dotnet tool install Digitals.OpenApi        # or add it to .config/dotnet-tools.json
dt-openapi lint contract/openapi.yaml       # exit 1 on any finding
dt-openapi diff contract/openapi.yaml src/Acme.Api/openapi.json
dt-openapi docs contract/openapi.yaml       # writes contract/openapi.html
dt-openapi cases contract/openapi.yaml      # the test cases it implies
```

| Command | Does |
| --- | --- |
| `lint <spec>` | Fails when the document breaks a rule, and lists every finding with its file, line and JSON path. Any path or URL, so it audits existing APIs too. A spec outside the current directory is shown by its absolute path. |
| `report <spec> [<out.html>]` | Writes an HTML report you can open and share. |
| `dashboard <spec>` | Browses the findings in the terminal. |
| `docs <spec> [<out.html>]` | Writes the contract as a Swagger UI page, by default beside the spec (`contract/openapi.html`). Swagger UI and the contract are inlined, so the page opens from the file with no network and can be sent as an attachment. A contract split across files needs bundling first (`dt-openapi vacuum bundle`). |
| `diff <design> <implementation>` | Fails when the implementation differs from the design. Wording, examples, titles, `x-dt-unconditional-write` and `x-dt-no-idempotency-key` may differ. |
| `breaking <base> <revision>` | Fails on a breaking change between two versions of a contract. Removing an operation needs it deprecated first with an `x-sunset` date that has passed, and a new sunset must be at least 180 days out; `--deprecation-days-stable <days>` changes that. A new value in a response enum is not breaking, since clients tolerate values they don't know. |
| `cases <spec>` | Lists the test cases the contract implies, each with an ID to name its test by. See [Test cases](#test-cases). |
| `changelog <base> <revision>` | Lists the changes. Add `--format html` for a page. |
| `ruleset [<out.yaml>]` | Prints the ruleset. With a path it writes `digitals.yaml`, the baseline it extends and the `functions` folder its JavaScript rules need, side by side. |
| `vacuum <args>`, `oasdiff <args>` | Runs the bundled tool directly. |

[examples/openapi.yaml](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/openapi.yaml) is a full HTTP API that passes every rule: page paging with `totalCount`, idempotent creates, `ETag` and `If-Match`, a merge patch, `:cancel` and `:search` custom methods, a multipart upload, a file download, a deprecated operation, OAuth scopes, links, a `oneOf` with a discriminator and a webhook. Copy from it, or open [examples/openapi.html](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/openapi.html), the page `dt-openapi docs examples/openapi.yaml` writes from it.

Options after the arguments go to the underlying tool, for example `dt-openapi lint api.yaml --github-annotations`. `lint`, `report`, `dashboard` and `ruleset` take `--baseline` for the baseline ruleset. `lint`, `report` and `dashboard` take `--ruleset <file>` to use your own ruleset instead.

The conventions behind the rules are in `docs/api-conventions.md` in csharp-api-template.

## Test cases

`dt-openapi cases` reads a contract and lists the tests it implies, for you or an agent to write in any language and test framework. It writes no code.

```text
CancelBooking  POST /api/v1/bookings/{bookingId}:cancel
  CancelBooking.200                        Send the byCustomer example. Expect 200 with the ETag, RateLimit-Policy and RateLimit headers and a Booking body.
  CancelBooking.404                        Use an unknown bookingId. Expect 404 with the RateLimit-Policy and RateLimit headers and a ProblemDetails body as application/problem+json.
  CancelBooking.412                        Send an If-Match ETag that no longer matches. Expect 412 with the RateLimit-Policy and RateLimit headers and a ProblemDetails body as application/problem+json.
  CancelBooking.428                        Leave out If-Match. Expect 428 with the RateLimit-Policy and RateLimit headers and a ProblemDetails body as application/problem+json.
  CancelBooking.path.bookingId.maxLength   Accepts 36 characters, rejects 37 with 422.
  CancelBooking.path.bookingId.format      Rejects "not-a-uuid" with 422.
  CancelBooking.header.If-Match.required   Rejects a request without If-Match with 400.
```

There are two kinds of case:

- One per documented response, `<operationId>.<status>`. It says how to bring the response about and what to expect: the headers, and the body schema with its media type when that isn't `application/json`.
- One per rule on each parameter and request body field, `<operationId>.<location>.<field>.<rule>`, where the location is `path`, `query`, `header`, `cookie` or `body`. A boundary case names the value to accept and the one to reject, so a test covers both sides: `minLength` and `maxLength`, `minimum`, `maximum` and their exclusive forms, `multipleOf`, `pattern`, `format`, `enum` and `const`, `minItems`, `maxItems` and `uniqueItems`, `minProperties` and `maxProperties`, `required`, the type, a field the schema doesn't define, and `oneOf` and `anyOf`. Nested objects and array items are followed (`requests[].maxLength`), through `allOf` and `$ref`.

A request the service can't read expects 400: a value of the wrong type, a field it doesn't know, a missing parameter. A request it reads but that breaks a rule expects 422, and so does a missing body field. When an operation documents only one of the two, both kinds expect that one. Nothing is listed for what can't fail: a path parameter is always sent, a read-only field is the service's, and in a URL or a form a string can't be the wrong type.

`--format json` prints the cases as a list of `id`, `operation`, `method`, `path`, `status` and `detail`, for an agent. `--only responses` or `--only rules` lists one kind. An ID starts with the `operationId`. An operation without one, common in a contract Swashbuckle generates, gets a name made from its method and route instead: `POST /api/v1.0/orders/{orderId}:cancel` becomes `PostOrdersByOrderIdCancel`, without `api` or the version, and a number is added when another operation has the name. The command warns when it does this, since a name made from the route changes when the route does. Swagger 2.0 works too.

## Rules

There are two rulesets, and any rule left on fails `lint`:

- `digitals-baseline.yaml`, used with `--baseline`, is the industry basics every HTTP API must meet whatever its design: a valid document, the OWASP API Security Top 10 rules, documented operations, valid examples and no personal data in a URL. Audit an existing API with it.
- `digitals.yaml`, the default, extends the baseline with the Digitals house rules for resources, naming, paging, concurrency, errors and documentation. A new contract must pass it.

The [OpenAPI rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/openapi.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

Both rulesets list every vacuum built-in, each set to `error` or turned `off` with the reason, so a vacuum upgrade never switches a rule on unseen.

To skip a rule for one document, write your own ruleset that extends `digitals.yaml` or the baseline and pass it with `--ruleset`. `dt-openapi` passes the bundled `functions` folder along. List `spectral:owasp` again: vacuum doesn't follow it through a file it extends, and would drop every OWASP rule. Running vacuum yourself on that ruleset needs `--functions ./functions`.

```yaml
extends:
  - ./digitals.yaml          # from `dt-openapi ruleset digitals.yaml`
  - [spectral:owasp, all]
rules:
  owasp-no-credentials-in-url: off   # why
```

Each run asks nuget.org, at most once a day, whether this version is vulnerable or deprecated, and if it is, prints the advisory and how to upgrade to stderr. Set `DT_FAIL_ON_WITHDRAWN=1` to fail the run instead, `DT_VERSION_CHECK=off` to skip the check, or `DT_NUGET_REGISTRATION` to a mirror's registration base URL. Without a network it says nothing.

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64). vacuum is MIT licensed and oasdiff Apache-2.0; their licences ship beside the binaries in `native/`.

Swagger UI is Apache-2.0 licensed; its licence ships in `viewer/`.
