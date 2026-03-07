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
| `docs/implementation-plan.md` | Phased implementation plan with deliverables |
| `docs/adr/` | Architecture Decision Records — **read these before making design changes** |
| `fennath.yaml.example` | Reference configuration (once created) |

## Repository Layout

```
fennath/
├── src/Fennath/              # Main application source
│   ├── Configuration/        # YAML config model and loading
│   ├── Proxy/                # YARP integration and health checks
│   ├── Certificates/         # ACME/Let's Encrypt cert management
│   ├── Dns/                  # Loopia XML-RPC DNS provider
│   ├── Discovery/            # Route discovery (static + Docker)
│   └── Telemetry/            # OpenTelemetry setup
├── tests/Fennath.Tests/      # Unit and integration tests
├── docs/                     # Design documents and ADRs
├── Dockerfile                # Container build
└── docker-compose.yaml       # Deployment descriptor
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
- Follow standard .NET naming conventions (PascalCase for public members, camelCase for locals).
- Prefer `ILogger<T>` for all logging — logs are exported via OpenTelemetry.
- Use dependency injection throughout. Register services in `Program.cs`.
- Keep classes focused — one responsibility per class.

### Configuration
- All user-facing configuration goes through `fennath.yaml`, parsed into strongly-typed
  `FennathConfig` classes.
- Sensitive values must use environment variable substitution (`${VAR_NAME}`), never hardcoded.
- Config changes should be hot-reloadable where possible (no restart required).

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
- `IRouteDiscovery` — route source (static config, Docker labels)

### Building and Running
```bash
# Build
dotnet build src/Fennath/

# Run locally (development)
dotnet run --project src/Fennath/

# Run tests
dotnet test

# Docker build
docker build -t fennath .

# Docker Compose deployment
docker compose up -d
```

### Dependencies
- **YARP** (`Yarp.ReverseProxy`) — reverse proxy engine
- **Certes** — ACME v2 client for Let's Encrypt (targets .NET Standard 2.0; see ADR-002)
- **YamlDotNet** — YAML configuration parsing
- **OpenTelemetry .NET SDK** — traces, metrics, logs export

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
