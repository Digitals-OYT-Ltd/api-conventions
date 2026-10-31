# Digitals.Protobuf

`dt-protobuf` lints the `.proto` files of a gRPC API against the Digitals rules, checks their formatting, fails on breaking changes, and writes them as a documentation page. It bundles [buf](https://github.com/bufbuild/buf), [protoc-gen-doc](https://github.com/pseudomuto/protoc-gen-doc) and [protovalidate](https://protovalidate.com), so nothing else needs installing.

```sh
dotnet tool install Digitals.Protobuf       # or add it to .config/dotnet-tools.json
dt-protobuf lint proto                      # exit 100 on any finding
dt-protobuf format proto                    # exit 100 when a file isn't formatted
dt-protobuf breaking .git#branch=main proto # exit 100 on a breaking change since main
dt-protobuf docs proto proto.html           # one page that opens offline
dt-protobuf cases proto                     # the test cases it implies
```

| Command | Does |
| --- | --- |
| `lint <input>` | Fails when a file breaks a rule, and lists every finding with its file, line and what to change. `--error-format json` adds the rule name. |
| `format <input>` | Fails when a file isn't in buf's format, and prints the diff. `--write` rewrites the files instead. |
| `breaking <base> <revision>` | Fails on a change from `base` to `revision` that breaks code generated from `base`. |
| `docs <input> [<out.html>]` | Writes one self-contained page, by default `<input's name>.html` in the current directory: each service with its RPCs first, every request and response field under its RPC, then the shared messages and enums. It needs no network, so it opens from a file or an attachment. |
| `cases <dir>` | Lists the test cases the contract implies, each with an ID to name its test by. See [Test cases](#test-cases). |
| `ruleset [<out.yaml>]` | Prints the buf config, or writes it, for example as a module's `buf.yaml` for the buf editor extensions. |
| `buf <args>` | Runs the bundled buf directly. |

An input is a directory of `.proto` files laid out by package, so `acme.notes.v1` lives in `acme/notes/v1/`. A git ref (`.git#branch=main`, `.git#tag=v1.2.0`) or an archive works too. Options after the arguments go to buf, for example `dt-protobuf lint proto --error-format github-actions`. `lint` and `breaking` take `--baseline` for the baseline rules, or `--config <file>` for your own buf config. The bundled config replaces any `buf.yaml` in the input.

protovalidate ships in the tool, so a contract that imports `buf/validate/validate.proto` needs no `buf.yaml` dependency: every command compiles it with the bundled copy, and a contract that brings its own copy keeps it. For a directory that imports it, buf runs on a copy with `validate.proto` added. Findings still name your files, and `validate.proto` itself is neither linted nor documented.

## Test cases

`dt-protobuf cases` reads the `.proto` files in a directory and lists the tests they imply, for you or an agent to write in any language and test framework. It writes no code.

```text
BookingService.CreateBooking  unary /acme.bookings.v1.BookingService/CreateBooking
  BookingService.CreateBooking.OK                 Send a valid CreateBookingRequest. Expect OK with a Booking.
  BookingService.CreateBooking.ALREADY_EXISTS     Call it when the room is taken at that time. Expect ALREADY_EXISTS.
  BookingService.CreateBooking.retry              Send the same request twice with the same request_id. Expect the first result both times and the change made once.
  BookingService.CreateBooking.request_id.uuid    Rejects "not-a-uuid" with INVALID_ARGUMENT.
  BookingService.CreateBooking.venue_id.required  Rejects a request without venue_id with INVALID_ARGUMENT.
  BookingService.CreateBooking.venue_id.pattern   Accepts a value matching ^[A-Z]{3}[0-9]{3}$, rejects "!" with INVALID_ARGUMENT.
  BookingService.CreateBooking.guests.lte         Accepts 40, rejects 41 with INVALID_ARGUMENT.
  BookingService.CreateBooking.guests.gte         Accepts 1, rejects 0 with INVALID_ARGUMENT.
```

There are four kinds of case, each with an ID that starts with the service and the method:

- `<rpc>.OK`, a call that succeeds. It says what to send and what comes back, a stream or one message, for each kind of RPC: unary, client streaming, server streaming and bidirectional.
- `<rpc>.<STATUS>` for each gRPC status the method's own comment names, such as `NOT_FOUND`. When the comment says when (`NOT_FOUND when no booking has the ID`), the case says to call it then.
- `<rpc>.retry` when the request has a `request_id`, and `<rpc>.<field>.unknown_path` for each `google.protobuf.FieldMask` in it.
- `<rpc>.<field>.<rule>` for each [protovalidate](https://protovalidate.com) rule on the request, field by field through nested messages, lists (`tags[]`) and maps (`labels.keys`, `labels.values`). A request that breaks one gets `INVALID_ARGUMENT`. Each rule names a value to accept and one to reject: `max_len`, `gte`, `lt`, `pattern`, `in`, `const`, `defined_only` and the rest, every string format from `uuid` to `host_and_port`, durations and timestamps, `min_items` and `min_pairs`, `required`, required oneofs and CEL rules by their ID and message. A rule it can't explain, such as a predefined rule of your own, still gets a case that names it.

`cases` reads only `.proto` files, so any import other than protovalidate and the Google well-known types has to be in the directory.

`--format json` prints the cases as a list of `id`, `operation`, `kind`, `path`, `deprecated` and `detail`, for an agent. `--only calls` or `--only rules` lists one kind. [examples/protobuf.cases.txt](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/protobuf.cases.txt) is what it lists for the example contract.

## Rules

There are two levels, and any rule left on fails `lint`:

- `buf-baseline.yaml`, used with `--baseline`, is the industry basics: buf's `BASIC` category. One package per directory, the protobuf style guide's casing (`PascalCase` messages, `lower_snake_case` fields, `UPPER_SNAKE_CASE` enum values), no unused imports. Audit an existing API with it.
- `buf.yaml`, the default, is buf's `STANDARD` and `COMMENTS` categories. A new contract must pass it.

The [Protobuf rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/protobuf.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

[examples/protobuf](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/protobuf) is a gRPC contract that passes every rule: unary, server-streaming and client-streaming RPCs, page paging with `total_count`, an update mask, oneofs, a map, nested messages, `optional`, a deprecated RPC and field, and the well-known types. Copy from it, or open [examples/protobuf.html](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/examples/protobuf.html), the page `dt-protobuf docs examples/protobuf examples/protobuf.html` writes from it.

`breaking` uses buf's `FILE` category at both levels: any change that breaks code generated from the old files, which covers everything that breaks the wire and JSON formats too.

buf's rules are listed at [buf.build/docs/lint/rules](https://buf.build/docs/lint/rules) and [buf.build/docs/breaking/rules](https://buf.build/docs/breaking/rules).

Each run asks nuget.org, at most once a day, whether this version is vulnerable or deprecated, and if it is, prints the advisory and how to upgrade to stderr. Set `DT_FAIL_ON_WITHDRAWN=1` to fail the run instead, `DT_VERSION_CHECK=off` to skip the check, or `DT_NUGET_REGISTRATION` to a mirror's registration base URL. Without a network it says nothing.

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64). buf and protovalidate are Apache-2.0 licensed and protoc-gen-doc MIT licensed; their licences ship beside the binaries in `native/`.
