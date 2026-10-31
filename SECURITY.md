# Security

## Supported versions

Only the latest release of each tool gets fixes: `dt-openapi`, `dt-asyncapi` and `dt-protobuf`. A fix ships as a new release, never as a patch to an old one.

## Reporting a vulnerability

Report it privately at [Security > Report a vulnerability](https://github.com/Digitals-OYT-Ltd/api-conventions/security/advisories/new). Don't open a public issue. Say which tool and version, and how to reproduce it.

That covers the tools and everything they bundle: vacuum, oasdiff, Spectral, buf, protovalidate, Swagger UI and the AsyncAPI viewer. A vulnerability in a bundled tool that its own project has already fixed is something we only need to ship, and `.github/workflows/security.yml` looks for those every Monday and opens an issue.

## Withdrawing a vulnerable version

nuget.org can't delete a version, and an exact pin in `.config/dotnet-tools.json` restores it whatever we do. So we tell everyone on it, and make sure nothing new picks it up. In order:

1. Release the fix: tag `openapi/vX.Y.Z`, `asyncapi/vX.Y.Z` or `protobuf/vX.Y.Z` as usual.
2. Publish a GitHub security advisory from the report, or from [a new draft](https://github.com/Digitals-OYT-Ltd/api-conventions/security/advisories/new). Ecosystem NuGet, the package `Digitals.OpenApi` (or `Digitals.AsyncApi`, `Digitals.Protobuf`), the affected range such as `< 1.4.2`, and the patched version. Request a CVE there too. Once GitHub reviews it, it reaches the GitHub Advisory Database and nuget.org shows the version as vulnerable.
3. Deprecate the affected versions on nuget.org: the package's Manage page, Deprecation, reason Critical bugs, and the message `Upgrade to X.Y.Z: <advisory URL>`. This shows at once, without waiting on the review.
4. Unlist them with the Unlist workflow, which hides the pointer package and its six platform packages, so `dotnet tool install` and `dotnet tool update` skip them. It needs an API key that can unlist, which Trusted Publishing can't issue, so make one for this and delete it after:
   1. On nuget.org, API Keys, Create: owner the organisation, scope Unlist package only, glob patterns `Digitals.OpenApi*`, `Digitals.AsyncApi*` and `Digitals.Protobuf*`, and the shortest expiry.
   2. `gh secret set NUGET_UNLIST_KEY --repo Digitals-OYT-Ltd/api-conventions`, and paste the key.
   3. Run the Unlist workflow from the Actions tab, once per version.
   4. Delete the key on nuget.org, then `gh secret delete NUGET_UNLIST_KEY --repo Digitals-OYT-Ltd/api-conventions`.

Steps 2 and 3 are what users see. Each tool checks its own version against nuget.org once a day and prints the advisory and the deprecation message to stderr, with `upgrade with: dotnet tool update <package>`. A pipeline that sets `DT_FAIL_ON_WITHDRAWN=1` fails instead.
