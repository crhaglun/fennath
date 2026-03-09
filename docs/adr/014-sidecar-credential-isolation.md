# ADR-014: Sidecar Architecture — DNS/ACME Credential Isolation

## Status

Accepted

## Context

Fennath currently runs all functionality in a single container: reverse proxying (YARP),
Docker route discovery, DNS record management (Loopia), and ACME certificate provisioning
(Let's Encrypt). This means the internet-facing proxy process holds both:

- **Docker socket access** — powerful host-level capability
- **DNS API credentials** — can modify any DNS record in the zone

A compromise of the proxy (which accepts traffic on ports 80/443 from the public internet)
would expose both attack surfaces simultaneously. The README explicitly identified this risk
and proposed a sidecar split.

Additionally, the proxy only needs DNS credentials during certificate provisioning (every
~60 days) and never during normal operation. The Docker socket is only needed for route
discovery, not for DNS management.

## Decision

Split Fennath into two containers communicating via a shared Docker volume:

### Proxy container (`fennath`)

Responsibilities:
- TLS termination via YARP reverse proxy
- Docker label route discovery (requires Docker socket)
- Certificate loading and hot-reload from shared volume
- Route manifest publishing (writes `routes.json` to shared volume)
- Health checks and OpenTelemetry

Does **not** have: DNS credentials, ACME client, DNS provider

### Sidecar container (`fennath-sidecar`)

Responsibilities:
- ACME certificate provisioning and renewal (writes certs to shared volume)
- DNS A record management via Loopia API
- Public IP monitoring and change detection
- Route manifest consumption (reads `routes.json` from shared volume for DNS reconciliation)
- OpenTelemetry

Does **not** have: Docker socket access

### Communication via shared volume

```
/data/shared/
├── certs/
│   ├── wildcard.pfx        # Written by sidecar, watched by proxy
│   └── acme-account.pem    # ACME account key (sidecar only)
└── routes.json             # Written by proxy, watched by sidecar
```

- **Certificate flow**: Sidecar provisions/renews cert → writes `wildcard.pfx` → Proxy
  detects change via `FileSystemWatcher` → reloads in-memory cert (zero-downtime via
  `ServerCertificateSelector`, per ADR-007).

- **Route/DNS flow**: Proxy discovers containers via Docker API → writes `routes.json`
  (list of active subdomains) → Sidecar detects change via `FileSystemWatcher` → sends
  `DnsCommand.SubdomainAdded` for new subdomains → DNS reconciliation creates A records.

### Project structure

```
src/
├── Fennath/                # Proxy container
├── Fennath.Sidecar/        # Sidecar container
└── Fennath.Shared/         # Shared types (config models, CertificateStore, metrics)
```

A shared class library (`Fennath.Shared`) holds types used by both containers:
configuration models, `CertificateStore`, `FennathMetrics`, `IDnsProvider` interface,
and the routes manifest model. This prevents type drift while keeping each container
focused on its responsibilities.

## Consequences

### Benefits

- **Reduced blast radius**: Proxy compromise doesn't expose DNS credentials; sidecar
  compromise doesn't provide Docker socket access.
- **Principle of least privilege**: Each container only has the capabilities it needs.
- **Independent scaling/restart**: Sidecar can restart for cert renewal without affecting
  proxy traffic. Proxy can restart without re-provisioning certificates.
- **Cleaner security audit**: Credential exposure is explicitly scoped per container in
  `docker-compose.yaml`.

### Trade-offs

- **Operational complexity**: Two containers instead of one. Docker Compose manages this
  naturally, but it's more to monitor.
- **Startup ordering**: Proxy must wait for certificate to appear on shared volume before
  accepting HTTPS traffic. Handled by polling the cert file on startup.
- **File-based communication**: Slightly higher latency than in-process channels for route
  changes (seconds instead of milliseconds). Acceptable for homelab use — DNS propagation
  is already the bottleneck.
- **Shared volume dependency**: Both containers must mount the same volume. Standard
  Docker Compose capability.

### ADR compatibility

- **ADR-001** (YARP): Unchanged — proxy still uses YARP.
- **ADR-002** (Certes): Unchanged — sidecar uses Certes for ACME.
- **ADR-003** (Wildcard cert): Unchanged — same cert strategy, just provisioned by sidecar.
- **ADR-004** (Loopia): Unchanged — sidecar holds Loopia credentials.
- **ADR-005** (Docker discovery): Unchanged — proxy still polls Docker API.
- **ADR-007** (Zero-downtime rotation): Enhanced — `FileSystemWatcher` triggers reload
  instead of in-process `StoreCertificate()` call.
- **ADR-010** (Cert persistence): Unchanged — same disk format, shared volume.
- **ADR-013** (DNS reconciliation): Modified — subdomain discovery communicated via
  `routes.json` instead of in-process `DnsCommandChannel`.
