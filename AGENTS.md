# AGENTS.md — Guidance for AI Agents Working on Fennath

This file provides context and expectations for AI coding agents (GitHub Copilot, etc.)
contributing to this project. It should be updated as the project matures.

## Project Overview

Fennath is a TLS-terminating reverse proxy for homelab use, built with .NET 10 and YARP.
See [README.md](README.md) for a human-oriented overview and
[docs/implementation-plan.md](docs/implementation-plan.md) for the full build plan.

## Key Documents

| Document | Purpose |
|----------|---------|
| `README.md` | Human-oriented project overview |
| `AGENTS.md` | This file — agent guidance and expectations |
| `docs/adr/` | Architecture Decision Records — **read these before making design changes** |
| `docs/requirements.md` | Functional and non-functional requirements |
| `docker/.env.example` | Reference configuration for Docker deployment |
| `src/Fennath.Proxy/appsettings.example.json` | Reference configuration for local development |

## Repository Layout

```
fennath/
├── src/Fennath.Proxy/         # Proxy container — YARP routing, TLS termination (no Docker socket)
│   ├── Proxy/                # YARP setup, config validator, cert file watcher
│   └── Telemetry/            # OpenTelemetry setup and proxy metrics middleware
├── src/Fennath.Operator/      # Operator container — Docker discovery, DNS, Docker discovery, DNS, ACME certs
│   ├── Discovery/            # Docker route discovery, proxy config writer (YARP JSON)
│   ├── Certificates/         # ACME/Let's Encrypt cert management
│   ├── Dns/                  # DNS management: IP monitoring, reconciliation, Loopia provider
│   └── Telemetry/            # OpenTelemetry setup for operator
├── src/Fennath.Shared/       # Shared library — types used by both containers
│   ├── Configuration/        # CertificateStoreOptions (shared cert-store config)
│   ├── Certificates/         # CertificateStore (in-memory + disk, with file-watch reload)
│   ├── Discovery/            # IRouteDiscovery interface, DiscoveredRoute record
│   └── Telemetry/            # FennathMetrics (custom OTel instruments)
├── tests/Fennath.Tests/      # Unit and integration tests
├── docs/                     # Design documents and ADRs
├── docker/
│   ├── Dockerfile                # Proxy container build
│   ├── Dockerfile.operator        # Operator container build
│   └── docker-compose.yaml       # Deployment descriptor (both containers)
```

## Conventions and Expectations

### Architecture Decisions
- All significant design decisions are recorded in `docs/adr/`.
- Before changing a technology choice, library, or architectural pattern, check if there is
  an existing ADR. If your change contradicts an ADR, create a new ADR that supersedes it
  rather than silently diverging.
- When making a new significant decision, create a new ADR following the format in
  `docs/adr/README.md`.

### Code Style
- Target: .NET 10 / C# 14. Use modern language features (file-scoped namespaces,
  primary constructors, pattern matching, etc.).
- **Prefer primary constructors** for DI classes (services, controllers, middleware).
  Use classic constructors only when you need constructor logic beyond field assignment.
- Follow standard .NET naming conventions (PascalCase for public members, camelCase for locals).
- Prefer `ILogger<T>` for all logging — logs are exported via OpenTelemetry.
- Use dependency injection throughout. Register services in `Program.cs`.
- Keep classes focused — one responsibility per class.

### Configuration
- Configuration uses the standard .NET `IConfiguration` / `IOptions<T>` pattern,
  bound from the `"Fennath"` section in `appsettings.json` (or environment variables,
  command-line args, etc.).
- Sensitive values should use environment variables (`Fennath__Dns__Loopia__Password`),
  user-secrets, or another secure provider — never hardcoded in config files.
- Config changes are hot-reloadable via `IOptionsMonitor<T>`.

### Testing
- Follow ADR-012 strictly: **integration tests over unit tests, behavior over implementation**.
- Tests must assert on observable outcomes ("request arrives at backend"), not internal state.
- Litmus test: "If I completely rewrite the internals but keep the same behavior, does this
  test still pass?" If no, the test is a change detector — rewrite or delete it.
- Do NOT write tests that just verify mocks were called in the right order.
- Do NOT write tests that only exist to increase coverage numbers.
- Use `FennathTestHost` and `TestBackend` for integration tests (real HTTP through YARP).
- Use Testcontainers for Docker discovery tests.
- Tag slow tests (ACME staging, Testcontainers) so they can be skipped in fast-feedback loops.
- Run tests with `dotnet test` from the repository root.

### Key Interfaces
These abstractions are central to the architecture — implementations can be swapped, but
the interfaces should remain stable:

- `IDnsProvider` — DNS record management (Loopia is the current implementation)
- `IRouteDiscovery` — route discovery source (Docker labels in operator; defined in Fennath.Shared)

### Building and Running
```bash
# Build all projects
dotnet build

# Run proxy locally (development)
dotnet run --project src/Fennath.Proxy/

# Run operator locally (development)
dotnet run --project src/Fennath.Operator/

# Run tests
dotnet test

# Format code (always run before committing)
dotnet format

# Docker build (proxy)
docker build -t fennath -f docker/Dockerfile .

# Docker build (operator)
docker build -t fennath-operator -f docker/Dockerfile.operator .

# Docker Compose deployment (both containers)
docker compose -f docker/docker-compose.yaml up -d
```

### Dependencies
- **YARP** (`Yarp.ReverseProxy`) — reverse proxy engine
- **Certes** — ACME v2 client for Let's Encrypt (targets .NET Standard 2.0; see ADR-002)
- **DnsClient** (`DnsClient.NET`) — DNS queries for ACME challenge propagation verification
- **OpenTelemetry .NET SDK** — traces, metrics, logs export (see ADR-006)
- **Microsoft.Extensions.Http.Resilience** — retry/circuit-breaker for outbound HTTP (Loopia API, IP echo services)
- **TUnit** — test framework

### Package Management
This project uses **Central Package Management** (`Directory.Packages.props`).

**Always use the `dotnet` CLI to add, remove, or update packages.** Do not hand-edit
`Directory.Packages.props` or `PackageReference` entries in `.csproj` files.

```bash
# Add a package to a project (automatically updates Directory.Packages.props)
dotnet add src/Fennath package SomePackage

# Add a package to the test project
dotnet add tests/Fennath.Tests package SomeTestPackage

# Update a package version
dotnet add src/Fennath package SomePackage --version 2.0.0

# List outdated packages
dotnet list package --outdated
```

The CLI ensures `Directory.Packages.props` and the project files stay in sync. Manual edits
risk version mismatches and missing entries.

## Maintaining This File

**This file should be updated as the project evolves.** Specifically:

- When new source directories or key files are added, update the repository layout section.
- When new conventions are established (error handling patterns, middleware ordering, etc.),
  document them here.
- When new interfaces or abstractions are introduced, add them to the "Key Interfaces" section.
- When build/test/deploy commands change, update the "Building and Running" section.
- When new dependencies are added, document them and their purpose.

If you are an AI agent and you make a structural change to the project (new directory, new
abstraction, new build step), update this file as part of the same change.
