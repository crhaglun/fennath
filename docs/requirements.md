# Fennath — Requirements (Draft)

> This document captures the functional and non-functional requirements for Fennath,
> derived from the ADRs, codebase, and README. Items marked **[aspirational]** are
> described in ADRs but not yet implemented in code.

## 1. Purpose

Fennath is a TLS-terminating reverse proxy for homelab use. It sits on the edge of
a home network, accepts HTTPS traffic from the internet, and forwards it as plain
HTTP to backend services running in Docker containers. It automates TLS certificate
provisioning, DNS record management, and route discovery so backend services can
remain simple.

## 2. Functional Requirements

### FR-0: Core Value Proposition

When a new Docker container with an HTTP endpoint is created and labeled with a
Fennath subdomain, Fennath will automatically ensure a public-facing route is
created and secured with HTTPS — without any manual DNS, certificate, or proxy
configuration.

This is the single end-to-end guarantee that all other requirements exist to
support.

### FR-1: Reverse Proxy

| ID | Requirement | Source |
|----|-------------|--------|
| FR-1.1 | Accept HTTPS connections on a configurable port and forward requests as plain HTTP to backend services | ADR-001 |
| FR-1.2 | Route requests to backends based on the Host header (SNI hostname matching) | ADR-005 |
| FR-1.3 | Support the apex/root domain (`@`) as a routable host alongside subdomains | Code |
| FR-1.4 | Return HTTP 404 for requests to unrecognized hosts | Code |
| FR-1.5 | Redirect all HTTP traffic to HTTPS unconditionally | Code |
| FR-1.6 | Expose a `/healthz` endpoint returning HTTP 200 when the proxy is running | Code |

### FR-2: TLS Certificates

| ID | Requirement | Source |
|----|-------------|--------|
| FR-2.1 | Provision a wildcard TLS certificate (`*.domain`) from Let's Encrypt via ACME DNS-01 challenge | ADR-003 |
| FR-2.2 | Persist certificates to disk as PFX files so they survive restarts | ADR-010 |
| FR-2.3 | Load persisted certificates on startup; skip provisioning if a valid cert exists | ADR-010 |
| FR-2.4 | Renew certificates automatically when they are within a configurable threshold of expiry (default: 30 days) | ADR-007 |
| FR-2.5 | Swap certificates in memory without restarting the proxy (zero-downtime rotation) | ADR-007 |
| FR-2.6 | Support Let's Encrypt staging mode for development/testing | ADR-010 |
| FR-2.7 | Persist the ACME account key so the same account is reused across restarts | ADR-010 |
| FR-2.8 | **[aspirational]** Support per-subdomain certificate overrides that take precedence over the wildcard | ADR-007, ADR-010 |
| FR-2.9 | **[aspirational]** Write a `metadata.json` alongside certs with expiry, issuer, and domains | ADR-010 |

### FR-3: DNS Management

| ID | Requirement | Source |
|----|-------------|--------|
| FR-3.1 | Detect the public IP address by polling external echo services | ADR-009 |
| FR-3.2 | Create/update A records for each managed subdomain when the public IP changes | ADR-013 |
| FR-3.3 | Create A records for new subdomains as they are discovered (if the IP is known) | ADR-013 |
| FR-3.4 | Always manage an A record for the apex domain (`@`) | ADR-013 |
| FR-3.5 | Create and clean up TXT records for ACME DNS-01 challenges | ADR-003 |
| FR-3.6 | All DNS operations go through the Loopia XML-RPC API | ADR-004 |
| FR-3.7 | Never delete A records on container stop — cleanup is conservative | ADR-013 |
| FR-3.8 | **[aspirational]** Run a 24-hour fallback reconciliation to clean up stale records | ADR-013 |

### FR-4: Route Discovery

| ID | Requirement | Source |
|----|-------------|--------|
| FR-4.1 | Discover routes from running Docker containers via `fennath.subdomain` label | ADR-005 |
| FR-4.2 | Derive backend URL from container name and optional `fennath.port` label (default: 80) | ADR-005, Code |
| FR-4.3 | Support comma-separated subdomains in a single label (e.g., `@,www`) | Code |
| FR-4.4 | Poll the Docker API periodically to detect container start/stop (default: 15s) | ADR-011 |
| FR-4.5 | Update YARP routing configuration dynamically without restart | ADR-011 |
| FR-4.6 | Deduplicate routes when multiple sources claim the same subdomain (first wins) | Code |
| FR-4.7 | Notify the DNS subsystem when new subdomains are discovered | ADR-013 |

### FR-5: Observability

| ID | Requirement | Source |
|----|-------------|--------|
| FR-5.1 | Export distributed traces via OTLP with W3C TraceContext propagation | ADR-006 |
| FR-5.2 | Export custom metrics: request count/duration per route, DNS updates, IP changes, cert expiry | ADR-006 |
| FR-5.3 | Export structured logs via OpenTelemetry | ADR-006 |
| FR-5.4 | Telemetry export is no-op when no OTLP endpoint is configured | ADR-006 |

## 3. Non-Functional Requirements

### NFR-1: Deployment

| ID | Requirement | Source |
|----|-------------|--------|
| NFR-1.1 | Run as a single Docker container via Docker Compose | ADR-001 |
| NFR-1.2 | Support a single apex domain with unlimited subdomains | ADR-003 |
| NFR-1.3 | Run as a non-root user inside the container | Dockerfile |
| NFR-1.4 | Use a read-only container filesystem (writable volumes for certs and tmp only) | docker-compose.yaml |
| NFR-1.5 | Require read-only access to the Docker socket | ADR-005 |

### NFR-2: Configuration

| ID | Requirement | Source |
|----|-------------|--------|
| NFR-2.1 | All configuration via environment variables following .NET `__` convention | README |
| NFR-2.2 | Validate configuration at startup; fail fast on invalid values | Code |
| NFR-2.3 | Sensitive values (passwords, API keys) must come from environment variables, not config files | AGENTS.md |
| NFR-2.4 | Support hot-reload of configuration via `IOptionsMonitor` | AGENTS.md |
| NFR-2.5 | Provide sensible defaults for all optional settings | Code |

### NFR-3: Reliability

| ID | Requirement | Source |
|----|-------------|--------|
| NFR-3.1 | Graceful shutdown: drain in-flight requests for up to 30 seconds before terminating | Code |
| NFR-3.2 | Resilient IP detection: try multiple echo services, use the first valid response | ADR-009 |
| NFR-3.3 | Survive cert provisioning failure on startup by logging and exiting with a non-zero code | Code |
| NFR-3.4 | Survive DNS/ACME failures during renewal; log errors and retry on next cycle | Code |
| NFR-3.5 | Survive Docker API failures; log and retry on next poll cycle | Code |
| NFR-3.6 | Certificate persistence prevents unnecessary Let's Encrypt requests (rate limit safety) | ADR-010 |

### NFR-4: Performance

| ID | Requirement | Source |
|----|-------------|--------|
| NFR-4.1 | Modest resource footprint suitable for homelab hardware (~30–50 MB memory) | ADR-001 |
| NFR-4.2 | O(1) certificate lookup per TLS handshake (in-memory store) | ADR-007 |
| NFR-4.3 | Route changes applied without disrupting in-flight requests | ADR-011 |

### NFR-5: Security

| ID | Requirement | Source |
|----|-------------|--------|
| NFR-5.1 | No proxy-level authentication — backends are responsible for their own auth | ADR-008 |
| NFR-5.2 | TLS private keys stored on disk with restricted access | ADR-003, ADR-010 |
| NFR-5.3 | No secrets in configuration files — use environment variables or secret providers | AGENTS.md |

## 4. Explicit Non-Goals

These are deliberately out of scope per the ADRs:

| Non-Goal | Rationale | Source |
|----------|-----------|--------|
| Proxy-level authentication (basic auth, OAuth, SSO) | Backends own their auth; proxy is transparent | ADR-008 |
| HTTP-01 ACME challenges | DNS-01 only; simpler, supports wildcards | ADR-003 |
| Multi-registrar DNS support | Loopia only; other providers require new `IDnsProvider` | ADR-004 |
| Kubernetes Ingress discovery | Docker only; K8s is a future extension point | ADR-005 |
| Web UI for route management | Routes come from Docker labels only | ADR-005 |
| Multi-domain support | One domain per instance | ADR-003 |
| Clustering / multi-node | Single-instance homelab deployment | ADR-001 |

## 5. Configurable Parameters

### Required (no defaults)

| Parameter | Description |
|-----------|-------------|
| `Fennath__Domain` | Registered domain at your registrar (e.g., `my-domain-name.se`) |
| `Fennath__Dns__Loopia__Username` | Loopia API username |
| `Fennath__Dns__Loopia__Password` | Loopia API password |
| `Fennath__Certificates__Email` | Contact email for Let's Encrypt |

### Optional (with defaults)

| Parameter | Default | Description |
|-----------|---------|-------------|
| `Fennath__Subdomain` | *(empty)* | Subdomain prefix scoping all services (e.g., `lab` → `*.lab.my-domain-name.se`) |
| `Fennath__Server__HttpsPort` | 443 | HTTPS listen port |
| `Fennath__Server__HttpPort` | 80 | HTTP listen port |
| `Fennath__Certificates__Staging` | false | Use Let's Encrypt staging |
| `Fennath__Certificates__StoragePath` | /data/certs | Certificate storage directory |
| `Fennath__Certificates__RenewalCheckIntervalSeconds` | 86400 | How often to check cert expiry |
| `Fennath__Certificates__RenewalThresholdDays` | 30 | Days before expiry to trigger renewal |
| `Fennath__Certificates__DnsPropagationTimeoutSeconds` | 300 | Max time to wait for TXT record visibility at public resolvers |
| `Fennath__Certificates__DnsPropagationPollingIntervalSeconds` | 10 | How often to query resolvers during propagation check |
| `Fennath__Certificates__ChallengePollingIntervalSeconds` | 120 | ACME challenge poll interval |
| `Fennath__Dns__PublicIpCheckIntervalSeconds` | 300 | Public IP poll interval |
| `Fennath__Docker__SocketPath` | /var/run/docker.sock | Docker API socket path |
| `Fennath__Docker__PollIntervalSeconds` | 15 | Docker container poll interval |

## 6. Docker Label Contract

Containers opt in to routing by applying these labels:

| Label | Required | Default | Description |
|-------|----------|---------|-------------|
| `fennath.subdomain` | Yes | — | Comma-separated subdomains (e.g., `grafana`, `@,www`) |
| `fennath.port` | No | 80 | Backend port on the container |

The backend URL is derived as `http://{container_name}:{port}`. The container must be
reachable from Fennath's Docker network at that address.

## 7. Known Gaps Between ADRs and Implementation

These items are described in ADRs as decided but are not yet present in the codebase:

| ADR Claim | Status |
|-----------|--------|
| Per-subdomain certificate overrides (ADR-007, ADR-010) | Not implemented — only wildcard cert supported |
| `metadata.json` alongside PFX files (ADR-010) | Not implemented — no metadata file written |
| File permission enforcement on cert storage (ADR-010) | Not implemented — relies on container/OS defaults |
| Consensus-based IP detection from 2+ services (ADR-009) | Not implemented — uses first-success, not consensus |
| 24-hour stale DNS record cleanup reconciliation (ADR-013) | Not implemented — only event-driven reconciliation exists |
| `fennath.backend` and `fennath.healthcheck.*` Docker labels (ADR-005) | Not implemented — backend derived from container name, no healthcheck labels |
| Static route configuration via config file (implementation plan) | Not implemented — Docker labels are the only route source |
