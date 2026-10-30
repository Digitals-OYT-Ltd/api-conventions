# Digitals.Protobuf

`dt-protobuf` lints the `.proto` files of a gRPC API against the Digitals rules, checks their formatting, fails on breaking changes, and writes them as a documentation page. It bundles [buf](https://github.com/bufbuild/buf) and [protoc-gen-doc](https://github.com/pseudomuto/protoc-gen-doc), so nothing else needs installing.

```sh
dotnet tool install Digitals.Protobuf       # or add it to .config/dotnet-tools.json
dt-protobuf lint proto                      # exit 100 on any finding
dt-protobuf format proto                    # exit 100 when a file isn't formatted
dt-protobuf breaking .git#branch=main proto # exit 100 on a breaking change since main
dt-protobuf docs proto proto.html           # one page that opens offline
```

| Command | Does |
| --- | --- |
| `lint <input>` | Fails when a file breaks a rule, and lists every finding with its file, line and what to change. `--error-format json` adds the rule name. |
| `format <input>` | Fails when a file isn't in buf's format, and prints the diff. `--write` rewrites the files instead. |
| `breaking <base> <revision>` | Fails on a change from `base` to `revision` that breaks code generated from `base`. |
| `docs <input> [<out.html>]` | Writes one self-contained page, by default `<input's name>.html` in the current directory: each service with its RPCs first, every request and response field under its RPC, then the shared messages and enums. It needs no network, so it opens from a file or an attachment. |
| `ruleset [<out.yaml>]` | Prints the buf config, or writes it, for example as a module's `buf.yaml` for the buf editor extensions. |
| `buf <args>` | Runs the bundled buf directly. |

An input is a directory of `.proto` files laid out by package, so `acme.notes.v1` lives in `acme/notes/v1/`. A git ref (`.git#branch=main`, `.git#tag=v1.2.0`) or an archive works too. Options after the arguments go to buf, for example `dt-protobuf lint proto --error-format github-actions`. `lint` and `breaking` take `--baseline` for the baseline rules, or `--config <file>` for your own buf config. The bundled config replaces any `buf.yaml` in the input.

## Rules

There are two levels, and any rule left on fails `lint`:

- `buf-baseline.yaml`, used with `--baseline`, is the industry basics: buf's `BASIC` category. One package per directory, the protobuf style guide's casing (`PascalCase` messages, `lower_snake_case` fields, `UPPER_SNAKE_CASE` enum values), no unused imports. Audit an existing API with it.
- `buf.yaml`, the default, is buf's `STANDARD` and `COMMENTS` categories. A new contract must pass it.

The [Protobuf rules page](https://github.com/Digitals-OYT-Ltd/api-conventions/blob/main/docs/rules/protobuf.md) explains every rule: why it exists, a failing and a passing example, and the message it prints.

| Category | Fires on | Level |
| --- | --- | --- |
| `BASIC` | A package that doesn't match its directory, a type, field or file not in the style guide's casing, an unused import, a field or enum value name used twice | baseline |
| `STANDARD` | A package without a version suffix (`acme.notes.v1`), a service not named `...Service`, an RPC whose request and response aren't its own `GetNoteRequest` and `GetNoteResponse`, an enum value without the enum's prefix, an enum whose zero value doesn't end `_UNSPECIFIED`, `google.protobuf.Empty` as a request or response | full |
| `COMMENTS` | A service, RPC, message, field, oneof, enum or enum value with no comment above it | full |

[examples/protobuf](../../examples/protobuf) is a gRPC contract that passes every rule: unary, server-streaming and client-streaming RPCs, page paging with `total_count`, an update mask, oneofs, a map, nested messages, `optional`, a deprecated RPC and field, and the well-known types. Copy from it, or open [examples/protobuf.html](../../examples/protobuf.html), the page `dt-protobuf docs examples/protobuf examples/protobuf.html` writes from it.

`breaking` uses buf's `FILE` category at both levels: any change that breaks code generated from the old files, which covers everything that breaks the wire and JSON formats too.

buf's rules are listed at [buf.build/docs/lint/rules](https://buf.build/docs/lint/rules) and [buf.build/docs/breaking/rules](https://buf.build/docs/breaking/rules).

The package is platform-specific: `dotnet tool install` picks the build for your machine (macOS, Linux and Windows, x64 and Arm64). buf is Apache-2.0 licensed and protoc-gen-doc MIT licensed; their licences ship beside the binaries in `native/`.
