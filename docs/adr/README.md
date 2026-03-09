# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the Fennath project.

ADRs document significant technical decisions made during the design and development of Fennath.
Each record captures the context, decision, and consequences so that future contributors
(or future-you) can understand *why* things are the way they are.

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [001](001-dotnet10-yarp.md) | .NET 10 with YARP as reverse proxy | Accepted |
| [002](002-certes-acme-client.md) | Certes as ACME client library | Accepted |
| [003](003-wildcard-cert-dns01.md) | Wildcard certificate via DNS-01 challenge | Accepted |
| [004](004-loopia-xmlrpc-unified-dns.md) | Loopia XML-RPC API for all DNS management | Accepted |
| [005](005-route-discovery.md) | Static config + Docker label route discovery | Accepted |
| [006](006-opentelemetry-observability.md) | OpenTelemetry for full observability | Accepted |
| [007](007-cert-rotation-no-restart.md) | Zero-downtime certificate rotation via ServerCertificateSelector | Accepted |
| [008](008-no-proxy-auth.md) | No authentication at proxy level | Accepted |
| [009](009-external-ip-detection.md) | External IP detection via polling | Accepted |
| [010](010-cert-persistence-rate-limits.md) | Certificate persistence and rate limit mitigation | Accepted |
| [011](011-dynamic-route-updates.md) | Dynamic route updates via YARP InMemoryConfigProvider | Accepted |
| [012](012-testing-strategy.md) | Testing strategy — integration-heavy, behavior-focused | Accepted |
| [013](013-per-subdomain-dns-reconciliation.md) | Per-subdomain DNS with event-driven reconciliation | Accepted |
| [014](014-sidecar-credential-isolation.md) | Sidecar architecture — DNS/ACME credential isolation | Accepted |

## Format

Each ADR follows a lightweight format:

- **Status**: Proposed, Accepted, Deprecated, Superseded
- **Context**: What is the situation that motivates this decision?
- **Decision**: What is the change we are making?
- **Consequences**: What becomes easier or harder as a result?
