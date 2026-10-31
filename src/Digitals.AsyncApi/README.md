# Digitals.AsyncApi

`dt-asyncapi` lints an AsyncAPI 2 or 3 document, the contract of an event API, against the Digitals rules. It bundles [Spectral](https://github.com/stoplightio/spectral), so nothing else needs installing.

```sh
dotnet tool install Digitals.AsyncApi       # or add it to .config/dotnet-tools.json
dt-asyncapi lint contract/asyncapi.yaml     # exit 1 on any finding
dt-asyncapi cases contract/asyncapi.yaml    # the test cases it implies
```

| Command | Does |
| --- | --- |
| `lint <spec>` | Fails when the document breaks a rule, and lists every finding with its line and JSON path. Any path or URL, so it audits existing APIs too. |
| `ruleset [<out.yaml>]` | Prints the ruleset. With a path it writes `digitals.yaml` and the baseline it extends, side by side, for the Spectral editor extensions. |
| `docs <spec> [<out.html>]` | Writes the contract as a documentation page, by default beside the spec (`asyncapi.html`). The AsyncAPI viewer and the contract are inlined, so the page opens from the file with no network and can be sent as an attachment. |
| `cases <spec>` | Lists the test cases an AsyncAPI 3 contract implies, each with an ID to name its test by. See [Test cases](#test-cases). |
| `spectral <args>` | Runs the bundled Spectral directly. |

[examples/asyncapi.yaml](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/asyncapi.yaml) is a full event API that passes every rule: servers with variables, security and Kafka bindings, channel parameters, request and reply, message traits, correlation ids, `allOf` and `oneOf` payloads, and several examples per message. Copy from it, or open [examples/asyncapi.html](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/asyncapi.html), the page `dt-asyncapi docs examples/asyncapi.yaml` writes from it.

Options after the arguments go to Spectral, for example `dt-asyncapi lint asyncapi.yaml --format github-actions`. `lint` and `ruleset` take `--baseline` for the baseline ruleset, and `lint` takes `--ruleset <file>` to use your own instead.

There is no breaking-change check yet: no AsyncAPI diff tool ships as a standalone binary.

## Test cases

`dt-asyncapi cases` reads an AsyncAPI 3 contract and lists the tests it implies, for you or an agent to write in any language and test framework. It writes no code.

```text
PublishBookingConfirmed  send bookings.v1.confirmed
  PublishBookingConfirmed.BookingConfirmed  Make the service send a BookingConfirmed. Expect it on bookings.v1.confirmed with the messageId, sentTime and traceparent headers, a Kafka key and a Booking payload.

AnswerAvailabilityRequest  receive availability.v1.requests
  AnswerAvailabilityRequest.AvailabilityRequested                              Send the nextWeekVideo example to availability.v1.requests. Expect an AvailabilityReplied to the address in the request's replyTo header, with a correlationId header that pairs it with the request.
  AnswerAvailabilityRequest.duplicate                                          Send the same message twice. Expect it processed once.
  AnswerAvailabilityRequest.AvailabilityRequested.headers.messageId.required   Rejects a message without messageId. Expect it not processed.
  AnswerAvailabilityRequest.AvailabilityRequested.headers.messageId.format     Rejects "not-a-uuid". Expect it not processed.
```

There are three kinds of case:

- One per message an operation sends or receives, `<operation>.<message>`. For a message the service sends, it says what to expect on the channel: the headers, a Kafka key when the binding sets one, and the payload schema. For one it receives, it says what to send, the message's first example when it has one, and what to expect: the reply and where it goes, or that the message is processed.
- One per receive operation, `<operation>.duplicate`. Delivery is at least once, so the same message twice is processed once.
- One per rule on a received message's headers and payload, `<operation>.<message>.headers.<field>.<rule>` or `<operation>.<message>.payload.<field>.<rule>`. The rules and boundaries are the ones [`dt-openapi cases`](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/src/Digitals.OpenApi/README.md#test-cases) lists for a request body. A message that breaks one is not processed: the service doesn't act on it, whether it drops it or moves it to a dead-letter queue.

A sent message has no rule cases, since the service builds it and its test checks the payload against the schema. Message traits are applied first. A reply sent to an address the request names, such as `$message.header#/replyTo`, says so. A payload in another schema format, such as Avro, gets its message case but no rule cases.

`--format json` prints the cases as a list of `id`, `operation`, `action`, `channel` and `detail`, for an agent. `--only messages` or `--only rules` lists one kind. An ID starts with the operation's key, then the message's `name`, or its key under `messages` when it has none.

## Rules

There are two rulesets, and any finding fails `lint`:

- `digitals-baseline.yaml`, used with `--baseline`, is the industry basics: Spectral's whole `spectral:asyncapi` ruleset, which checks the document is valid, every operation and parameter is described, nothing defined goes unused, and every message example matches its schema. Audit an existing API with it.
- `digitals.yaml`, the default, extends the baseline with the Digitals house rules, the same ones the HTTP APIs follow. A new contract must pass it.

The [AsyncAPI rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/asyncapi.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

The event conventions themselves (channel naming, the message envelope, versioning) aren't decided yet, so no rule checks them.

`ruleset <path>` also writes `functions/`, the JavaScript behind `dt-message-example-valid`, beside the rulesets. Keep the three together.

Each run asks nuget.org, at most once a day, whether this version is vulnerable or deprecated, and if it is, prints the advisory and how to upgrade to stderr. Set `DT_FAIL_ON_WITHDRAWN=1` to fail the run instead, `DT_VERSION_CHECK=off` to skip the check, or `DT_NUGET_REGISTRATION` to a mirror's registration base URL. Without a network it says nothing.

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64; Windows Arm64 runs the x64 Spectral). Spectral is Apache-2.0 licensed; its licence ships beside the binary in `native/`. The AsyncAPI viewer (`@asyncapi/react-component`) is Apache-2.0 too; its licence ships in `viewer/`.
