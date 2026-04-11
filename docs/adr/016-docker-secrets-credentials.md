# ADR-016: Docker Secrets for Credential Management

## Status

Accepted

## Context

Fennath's operator architecture (ADR-014) isolates sensitive credentials from the
internet-facing proxy container. However, within the operator container, Loopia API
credentials are passed as plain environment variables — visible via `docker inspect`,
`/proc/*/environ`, and orchestrator APIs.

Docker secrets provide a more secure alternative: values are mounted as read-only
files at `/run/secrets/`, not exposed through the Docker API or process environment.

## Decision

Use Docker secrets for Loopia API credentials (`Username` and `Password`), read via
.NET's built-in `Microsoft.Extensions.Configuration.KeyPerFile` provider.

Both containers add a single line to their startup configuration:

```csharp
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
```

This reads each file in `/run/secrets/` as a configuration key-value pair (filename
is the key, content is the value, `__` is the hierarchy separator). The existing
`IOptions<OperatorConfig>` binding works unchanged.

The `optional: true` flag ensures the application still starts when no secrets
directory exists (local development, or users who prefer environment variables).

Secret files are declared in the Docker Compose `secrets:` section and stored
outside the repository (e.g. `~/.fennath-secrets/`).

## Consequences

### Benefits

- **Credentials not in process environment**: Not visible via `docker inspect` or
  `/proc/*/environ`.
- **Backwards compatible**: `optional: true` makes this a no-op when no secrets
  directory exists. Env vars continue to work for local development.
- **No config class changes**: Same `__` hierarchy separator as env vars.

### Trade-offs

- **File-based secret management**: Users create secret files on disk instead of
  editing `.env`. Slightly more setup for first deployment.
- **NuGet dependency**: `Microsoft.Extensions.Configuration.KeyPerFile` added to
  the operator project (already in the ASP.NET shared framework for the proxy).

### ADR compatibility

- **ADR-014** (Credential isolation): Strengthened — credentials no longer in the
  process environment even within the operator container.
