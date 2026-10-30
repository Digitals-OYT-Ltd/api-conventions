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

[examples/openapi.yaml](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/openapi.yaml) is a full HTTP API that passes every rule: page paging with `totalCount`, idempotent creates, `ETag` and `If-Match`, a merge patch, `:cancel` and `:search` custom methods, a multipart upload, a file download, a deprecated operation, OAuth scopes, links, a `oneOf` with a discriminator and a webhook. Copy from it, or open [examples/openapi.html](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/openapi.html), the page `dt-openapi swagger examples/openapi.yaml` writes from it.

Options after the arguments go to the underlying tool, for example `dt-openapi lint api.yaml --github-annotations`. `lint`, `report`, `dashboard` and `ruleset` take `--baseline` for the baseline ruleset. `lint`, `report` and `dashboard` take `--ruleset <file>` to use your own ruleset instead.

The conventions behind the rules are in `docs/api-conventions.md` in csharp-api-template.

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

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64). vacuum is MIT licensed and oasdiff Apache-2.0; their licences ship beside the binaries in `native/`.

Swagger UI is Apache-2.0 licensed; its licence ships in `viewer/`.
