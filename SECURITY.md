# Security policy

## Supported versions

| Version | Security support |
|---|---|
| 1.3.x | Supported |
| 1.2.x and earlier | Not supported |

Security fixes are released on the latest supported 1.3.x version. Users should
upgrade before reporting a problem that may already be fixed.

## Reporting a vulnerability

Do not disclose exploitable details in a public issue, pull request, discussion,
or social-media post.

Use GitHub's private vulnerability reporting for this repository:

<https://github.com/simplelambda/LambdaFlow/security/advisories/new>

Include, when possible:

- affected LambdaFlow version, operating system, and architecture;
- whether the issue affects the host, CLI, extension, SDK, generated project, or
  packaged application;
- minimal reproduction steps or a proof of concept;
- expected impact and required attacker capabilities;
- relevant logs with credentials, tokens, personal data, and private paths
  removed;
- any suggested mitigation or patch.

Reports will be reviewed on a best-effort basis. The maintainers may request
additional information, prepare a private fix, and coordinate publication of an
advisory. Do not assume a report is accepted or a release date is committed until
the maintainers confirm it.

## Security boundaries

- LambdaFlow intentionally executes the backend command selected by the
  application author. A malicious project or backend therefore has the same
  permissions as the user who runs it.
- `frontend.pak` integrity uses SHA-256 for tamper detection. It is not publisher
  authentication; production bundles should also be code-signed.
- StdIO backends must reserve stdout for protocol envelopes and send diagnostics
  to stderr.
- Debug mode enables capabilities and logs that are not intended for production.
- Application authors remain responsible for validating untrusted input,
  protecting secrets, updating dependencies, and defining an appropriate threat
  model.

## Disclosure

Please allow reasonable time to investigate and release a fix before public
disclosure. Once a fix is available, the maintainers may publish a GitHub
Security Advisory describing affected versions, impact, and remediation.
