# Digitals.AsyncApi

`dt-asyncapi` lints an AsyncAPI 2 or 3 document, the contract of an event API, against the Digitals rules. It bundles [Spectral](https://github.com/stoplightio/spectral), so nothing else needs installing.

```sh
dotnet tool install Digitals.AsyncApi       # or add it to .config/dotnet-tools.json
dt-asyncapi lint contract/asyncapi.yaml     # exit 1 on any finding
```

| Command | Does |
| --- | --- |
| `lint <spec>` | Fails when the document breaks a rule, and lists every finding with its line and JSON path. Any path or URL, so it audits existing APIs too. |
| `ruleset [<out.yaml>]` | Prints the ruleset. With a path it writes `digitals.yaml` and the baseline it extends, side by side, for the Spectral editor extensions. |
| `docs <spec> [<out.html>]` | Writes the contract as a documentation page, by default beside the spec (`asyncapi.html`). The AsyncAPI viewer and the contract are inlined, so the page opens from the file with no network and can be sent as an attachment. |
| `spectral <args>` | Runs the bundled Spectral directly. |

[examples/asyncapi.yaml](../../examples/asyncapi.yaml) is a full event API that passes every rule: servers with variables, security and Kafka bindings, channel parameters, request and reply, message traits, correlation ids, `allOf` and `oneOf` payloads, and several examples per message. Copy from it, or open [examples/asyncapi.html](../../examples/asyncapi.html), the page `dt-asyncapi docs examples/asyncapi.yaml` writes from it.

Options after the arguments go to Spectral, for example `dt-asyncapi lint asyncapi.yaml --format github-actions`. `lint` and `ruleset` take `--baseline` for the baseline ruleset, and `lint` takes `--ruleset <file>` to use your own instead.

There is no breaking-change check yet: no AsyncAPI diff tool ships as a standalone binary.

## Rules

There are two rulesets, and any finding fails `lint`:

- `digitals-baseline.yaml`, used with `--baseline`, is the industry basics: Spectral's whole `spectral:asyncapi` ruleset, which checks the document is valid, every operation and parameter is described, nothing defined goes unused, and every message example matches its schema. Audit an existing API with it.
- `digitals.yaml`, the default, extends the baseline with the Digitals house rules, the same ones the HTTP APIs follow. A new contract must pass it.

The [AsyncAPI rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/asyncapi.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

The baseline turns these `spectral:asyncapi` rules off: `asyncapi-info-contact`, `asyncapi-info-contact-properties`, `asyncapi-info-license` and `asyncapi-info-license-url` (contact and license are optional), `asyncapi-servers` (servers are optional), `asyncapi-tags-alphabetical`, and `asyncapi-latest-version`, which `digitals.yaml` turns back on. The full list is at [Spectral's AsyncAPI rules](https://github.com/stoplightio/spectral/blob/develop/docs/reference/asyncapi-rules.md).

| Rule | Fires on | Level |
| --- | --- | --- |
| `dt-message-example-valid` | An AsyncAPI 3 message example whose payload or headers don't match the message's schemas: wrong type, a value outside the enum, a missing required field, a string over its `maxLength`. Spectral's `asyncapi-message-examples` does this for AsyncAPI 2 only | baseline |
| `dt-no-personal-data-in-channel` | A channel parameter that names personal data: a national ID (NHS or NI number), date of birth, postcode, address, email, phone or name. A channel address ends up in broker logs and metrics. Send it in the payload | baseline |
| `asyncapi-latest-version` | A document on an older AsyncAPI version | full |
| `dt-description` | A channel, operation, message, component schema or payload field with no description | full |
| `dt-message-example` | A message with no `examples`. `dt-message-example-valid` checks each against the payload schema | full |
| `dt-property-casing` | A payload field that isn't camelCase | full |
| `dt-enum-casing` | A string enum value that isn't camelCase: `onHold` | full |
| `dt-timestamp-name`, `dt-date-name` | A `date-time` field not named `...Time`, or a `date` field not named `...Date` | full |
| `dt-string-limit`, `dt-array-limit` | A string with no `maxLength`, `enum` or `const`, or an array with no `maxItems` | full |

The event conventions themselves (channel naming, the message envelope, versioning) aren't decided yet, so no rule checks them.

`ruleset <path>` also writes `functions/`, the JavaScript behind `dt-message-example-valid`, beside the rulesets. Keep the three together.

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64; Windows Arm64 runs the x64 Spectral). Spectral is Apache-2.0 licensed; its licence ships beside the binary in `native/`. The AsyncAPI viewer (`@asyncapi/react-component`) is Apache-2.0 too; its licence ships in `viewer/`.
