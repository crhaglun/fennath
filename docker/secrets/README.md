# Docker Secrets

This directory holds secret files for Docker Compose deployment.
Each file contains a single credential value (no trailing newline).

## Required secrets

In multi-operator deployments, each operator needs its own credential files.
The compose file remaps them to `Fennath__Dns__Loopia__Username` / `Password`
inside each container via the `target:` directive.

| File | Description | Used by |
|------|-------------|---------|
| `lab-loopia-username` | Loopia API username for lab domain | operator-lab |
| `lab-loopia-password` | Loopia API password for lab domain | operator-lab |
| `apps-loopia-username` | Loopia API username for apps domain | operator-apps |
| `apps-loopia-password` | Loopia API password for apps domain | operator-apps |

## Setup

```bash
# Lab domain credentials
echo -n "user@loopiaapi" > lab-loopia-username
echo -n "your-api-password" > lab-loopia-password

# Apps domain credentials (if using a second operator)
echo -n "user@loopiaapi" > apps-loopia-username
echo -n "your-api-password" > apps-loopia-password

chmod 600 *-loopia-*
```

## How it works

Docker Compose mounts these files at `/run/secrets/<target>` inside the container.
The `target:` directive in docker-compose.yaml remaps human-readable filenames
(e.g., `lab-loopia-username`) to the config key .NET expects
(`Fennath__Dns__Loopia__Username`). The `AddKeyPerFile` configuration provider
reads them automatically.

See [ADR-016](../../docs/adr/016-docker-secrets-credentials.md) for the decision record.

## Important

- **All files in this directory except this README are gitignored.**
- Use `echo -n` (no trailing newline) to avoid whitespace issues in credentials.
