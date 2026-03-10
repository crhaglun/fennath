# ADR-014: Sidecar Architecture — Credential and Privilege Isolation

## Status

Accepted (revised)

## Context

Fennath currently runs all functionality in a single container: reverse proxying (YARP),
Docker route discovery, DNS record management (Loopia), and ACME certificate provisioning
(Let's Encrypt). This means the internet-facing proxy process holds both:

- **Docker socket access** — powerful host-level capability (root-equivalent)
- **DNS API credentials** — can modify any DNS record in the zone

A compromise of the proxy (which accepts traffic on ports 80/443 from the public internet)
would expose both attack surfaces simultaneously. The Docker socket in particular allows
reading environment variables of all containers, which would leak DNS credentials even if
they were in a separate process.

## Decision

Split Fennath into two containers communicating via a shared Docker volume:

### Proxy container (`fennath`)

Responsibilities:
- TLS termination via YARP reverse proxy
- Route configuration loading from shared volume (YARP JSON config file)
- Certificate loading and hot-reload from shared volume
- Health checks and OpenTelemetry

Does **not** have: Docker socket, DNS credentials, ACME client, DNS provider, Docker.DotNet

### Sidecar container (`fennath-sidecar`)

Responsibilities:
- Docker label route discovery (requires Docker socket)
- YARP proxy configuration generation (writes config JSON to shared volume)
- ACME certificate provisioning and renewal (writes certs to shared volume)
- DNS A record management via Loopia API
- Public IP monitoring and change detection
- OpenTelemetry

Does **not** have: Internet-exposed ports

### Communication via shared volume

```
/data/shared/
├── certs/
│   ├── wildcard.pfx        # Written by sidecar, watched by proxy
│   └── acme-account.pem    # ACME account key (sidecar only)
└── yarp-config.json        # Written by sidecar, watched by proxy
```

- **Route/config flow**: Sidecar discovers containers via Docker API → builds YARP-format
  JSON config → writes `yarp-config.json` atomically → Proxy loads via .NET's built-in
  `AddJsonFile(reloadOnChange: true)` → YARP's `LoadFromConfig()` automatically applies
  route changes via IConfiguration change tokens.

- **Certificate flow**: Sidecar provisions/renews cert → writes `wildcard.pfx` → Proxy
  detects change via `FileSystemWatcher` → reloads in-memory cert (zero-downtime via
  `ServerCertificateSelector`, per ADR-007).

- **DNS flow**: Sidecar discovers subdomains from Docker labels → sends
  `DnsCommand.SubdomainAdded` to DNS reconciliation service → creates A records.

### Project structure

```
src/
├── Fennath/                # Proxy container
├── Fennath.Sidecar/        # Sidecar container
└── Fennath.Shared/         # Shared types (config models, CertificateStore, metrics, IRouteDiscovery)
```

A shared class library (`Fennath.Shared`) holds types used by both containers:
configuration models, `CertificateStore`, `FennathMetrics`, `IRouteDiscovery` interface,
and `DiscoveredRoute` record. This prevents type drift while keeping each container
focused on its responsibilities.

## Consequences

### Benefits

- **True credential isolation**: Proxy has no Docker socket and no DNS credentials.
  A compromise of the internet-facing proxy cannot read environment variables of other
  containers or modify DNS records.
- **Principle of least privilege**: Proxy is a pure routing engine. Sidecar holds
  privileged access (Docker socket + DNS creds) but has no exposed ports.
- **Built-in config reload**: Uses .NET's native `AddJsonFile(reloadOnChange: true)`
  and YARP's `LoadFromConfig()` — no custom file watchers needed for route changes.
- **Independent scaling/restart**: Sidecar can restart for cert renewal without affecting
  proxy traffic. Proxy can restart without re-provisioning certificates.
- **Cleaner security audit**: No sensitive capabilities in the internet-facing container.

### Trade-offs

- **Operational complexity**: Two containers instead of one. Docker Compose manages this
  naturally, but it's more to monitor.
- **Startup ordering**: Proxy must wait for certificate to appear on shared volume before
  accepting HTTPS traffic. Handled by polling the cert file on startup.
- **File-based communication**: Slightly higher latency than in-process channels for route
  changes (seconds instead of milliseconds). Acceptable for homelab use — DNS propagation
  is already the bottleneck.
- **Shared volume dependency**: Both containers must mount the same volume. Standard
  Docker Compose capability. Proxy mounts it read-only.

### ADR compatibility

- **ADR-001** (YARP): Unchanged — proxy still uses YARP.
- **ADR-002** (Certes): Unchanged — sidecar uses Certes for ACME.
- **ADR-003** (Wildcard cert): Unchanged — same cert strategy, just provisioned by sidecar.
- **ADR-004** (Loopia): Unchanged — sidecar holds Loopia credentials.
- **ADR-005** (Docker discovery): Modified — Docker discovery moved from proxy to sidecar.
  Proxy reads YARP config from shared volume instead of polling Docker directly.
- **ADR-007** (Zero-downtime rotation): Enhanced — `FileSystemWatcher` triggers reload
  instead of in-process `StoreCertificate()` call.
- **ADR-010** (Cert persistence): Unchanged — same disk format, shared volume.
- **ADR-013** (DNS reconciliation): Modified — subdomain discovery is now in-process within
  the sidecar (DockerRouteDiscovery → ProxyConfigWriter → DnsCommandChannel).
