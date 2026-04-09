# Docker Secrets

This directory holds secret files for Docker Compose deployment.
Each file contains a single credential value (no trailing newline).

## Required secrets

| File | Description | Used by |
|------|-------------|---------|
| `Fennath__Dns__Loopia__Username` | Loopia API username | operator |
| `Fennath__Dns__Loopia__Password` | Loopia API password | operator |

## Setup

```bash
echo -n "user@loopiaapi" > Fennath__Dns__Loopia__Username
echo -n "your-api-password" > Fennath__Dns__Loopia__Password
chmod 600 Fennath__Dns__Loopia__*
```

## How it works

Docker Compose mounts these files at `/run/secrets/<filename>` inside the container.
The .NET `AddKeyPerFile` configuration provider reads them — the filename (with `__`
as hierarchy separator) maps directly to configuration keys. Existing `IOptions<T>`
bindings work unchanged.

See [ADR-016](../../docs/adr/016-docker-secrets-credentials.md) for the decision record.

## Important

- **All files in this directory except this README are gitignored.**
- Use `echo -n` (no trailing newline) to avoid whitespace issues in credentials.
