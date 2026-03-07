# Fennath — Implementation Plan

> A TLS-terminating reverse proxy with automatic Let's Encrypt certificates and DNS management
> for homelab use. Named after the Sindarin word for "doorways."

## Tech Stack

| Component          | Technology                                    |
|--------------------|-----------------------------------------------|
| Runtime            | .NET 10 LTS / C# 14                          |
| Reverse Proxy      | YARP (Yet Another Reverse Proxy)              |
| ACME Client        | Certes (ACME v2 library, .NET Standard 2.0)   |
| TLS Certificates   | Let's Encrypt via DNS-01 challenge            |
| DNS Management     | Loopia XML-RPC API                            |
| Config Format      | YAML (YamlDotNet)                             |
| Container Runtime  | Docker / Docker Compose                       |
| Observability      | OpenTelemetry SDK → Grafana Cloud (OTLP)      |
| Host OS            | Linux (directly on public internet)           |

See [ADRs](adr/README.md) for rationale behind each decision.

## Architecture Overview

```
Internet
  │
  ▼ HTTPS :443
┌────────────────────────────────────────────────────┐
│ Fennath                                             │
│                                                     │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────┐ │
│  │ YARP Proxy   │  │ Cert Manager │  │ DNS Manager │ │
│  │ TLS + Route  │  │ Certes ACME  │  │ Loopia     │ │
│  │              │  │ DNS-01       │  │ XML-RPC    │ │
│  └──────┬───────┘  └──────────────┘  └────────────┘ │
│         │                                            │
│  ┌──────┴───────────────────────────────────┐       │
│  │ Route Discovery                           │       │
│  │  • Static YAML config (primary)           │       │
│  │  • Docker label watcher (optional)        │       │
│  └───────────────────────────────────────────┘       │
│                                                     │
│  ┌───────────────────────────────────────────┐       │
│  │ OpenTelemetry → Grafana Cloud             │       │
│  │  traces + metrics + logs                  │       │
│  └───────────────────────────────────────────┘       │
└────────────────────────────────────────────────────┘
  │
  ▼ HTTP (private)
┌──────────┐ ┌──────────┐ ┌──────────┐
│ Service A │ │ Service B │ │ Service C │
└──────────┘ └──────────┘ └──────────┘
```

## Project Structure

```
fennath/
├── src/
│   └── Fennath/
│       ├── Program.cs
│       ├── Configuration/
│       │   ├── FennathConfig.cs              # Strongly-typed config model
│       │   └── ConfigLoader.cs               # YAML parsing + env var substitution
│       ├── Proxy/
│       │   ├── YarpConfigurator.cs            # Translates routes → YARP config
│       │   └── HealthCheckService.cs          # Backend health monitoring
│       ├── Certificates/
│       │   ├── AcmeService.cs                 # Certes-based ACME v2 client
│       │   ├── CertificateStore.cs            # In-memory + on-disk cert storage
│       │   ├── CertificateSelector.cs         # Kestrel ServerCertificateSelector
│       │   └── CertificateRenewalService.cs   # Background renewal (no restart)
│       ├── Dns/
│       │   ├── IDnsProvider.cs                # Abstraction for DNS providers
│       │   ├── LoopiaDnsProvider.cs           # Loopia XML-RPC implementation
│       │   ├── DnsUpdateService.cs            # Periodic IP check + record updates
│       │   └── PublicIpResolver.cs            # External IP detection
│       ├── Discovery/
│       │   ├── IRouteDiscovery.cs             # Abstraction for route sources
│       │   ├── StaticRouteDiscovery.cs        # From YAML config
│       │   └── DockerRouteDiscovery.cs        # From Docker labels
│       └── Telemetry/
│           └── TelemetrySetup.cs              # OTel SDK configuration
├── tests/
│   └── Fennath.Tests/
│       └── ...
├── docs/
│   ├── adr/                                   # Architecture Decision Records
│   └── implementation-plan.md                 # This file
├── Dockerfile
├── docker-compose.yaml
├── fennath.yaml.example
└── README.md
```

## Configuration Schema

```yaml
# fennath.yaml
domain: example.com

dns:
  provider: loopia
  loopia:
    username: user@loopiaapi
    password: ${LOOPIA_API_PASSWORD}        # env var substitution
  publicIpCheckIntervalSeconds: 300

certificates:
  email: admin@example.com
  wildcard: true                            # default wildcard cert
  staging: false                            # true = Let's Encrypt staging
  storagePath: /data/certs

routes:
  - subdomain: grafana
    backend: http://localhost:3000
    healthCheck:
      path: /api/health
      intervalSeconds: 30

  - subdomain: git
    backend: http://192.168.1.50:3000

  - subdomain: api
    backend: http://localhost:8080
    certificate:
      mode: individual                      # per-subdomain cert override

docker:
  enabled: true
  socketPath: /var/run/docker.sock

telemetry:
  endpoint: https://otlp-gateway-prod-xx.grafana.net/otlp
  protocol: grpc
  headers:
    Authorization: "Basic ${OTEL_AUTH_HEADER}"
  serviceName: fennath

server:
  httpsPort: 443
  httpPort: 80
  httpToHttpsRedirect: true
```

## Implementation Phases

### Phase 1: Foundation
**Goal:** A working HTTP reverse proxy with static config.

- [ ] Project scaffolding — .NET 10 solution, NuGet packages (YARP, YamlDotNet)
- [ ] Configuration model — strongly-typed C# classes for `fennath.yaml`
- [ ] YAML config loader with environment variable substitution
- [ ] YARP configurator — translate `FennathConfig.Routes` → YARP `RouteConfig`/`ClusterConfig`
- [ ] Basic HTTP reverse proxy (no TLS) with static routes
- [ ] Backend health checks

**Deliverable:** `dotnet run` forwards HTTP requests to backends based on `fennath.yaml`.

### Phase 2: TLS & Certificates
**Goal:** HTTPS with automatic Let's Encrypt wildcard cert.

- [ ] Loopia XML-RPC API client (`LoopiaDnsProvider` behind `IDnsProvider`)
- [ ] ACME DNS-01 challenge solver using Loopia
- [ ] Certes integration — account creation, CSR, certificate download
- [ ] Wildcard certificate provisioning from Let's Encrypt
- [ ] Certificate storage (PFX on disk + in-memory `ConcurrentDictionary`)
- [ ] Kestrel `ServerCertificateSelector` integration
- [ ] TLS termination working end-to-end
- [ ] Background certificate renewal service (auto-renew before expiry)

**Deliverable:** `https://grafana.example.com` works with a valid Let's Encrypt cert.

### Phase 3: DNS Management
**Goal:** Automatic DNS record management.

- [ ] Public IP detection via external service (e.g., `api.ipify.org`)
- [ ] Periodic IP check with change detection
- [ ] Automatic A record updates via Loopia API when IP changes
- [ ] Subdomain record management tied to route configuration

**Deliverable:** DNS records stay in sync with config and public IP automatically.

### Phase 4: Docker Discovery
**Goal:** Auto-register routes from Docker container labels.

- [ ] Docker socket watcher for container start/stop events
- [ ] `fennath.*` label parsing
- [ ] Dynamic route registration/deregistration in YARP
- [ ] Conflict resolution (static config wins)

**Deliverable:** `docker run --label fennath.subdomain=myapp ...` auto-creates the route.

### Phase 5: Observability
**Goal:** Full telemetry to Grafana Cloud.

- [ ] OpenTelemetry SDK setup with OTLP gRPC exporter
- [ ] Request traces with W3C TraceContext propagation
- [ ] Custom metrics (requests, latency, backend health, cert expiry, DNS updates)
- [ ] Structured log export via OTel
- [ ] Grafana Cloud integration verified

**Deliverable:** Grafana dashboards showing request flow, error rates, and system health.

### Phase 6: Hardening & Polish
**Goal:** Production-ready deployment.

- [ ] HTTP → HTTPS redirect
- [ ] Config file hot-reload (watch for changes, update YARP routes)
- [ ] Graceful shutdown (drain connections)
- [ ] Dockerfile (multi-stage build, minimal image)
- [ ] `docker-compose.yaml` for deployment
- [ ] `fennath.yaml.example` with documented options
- [ ] README.md with setup guide

**Deliverable:** `docker compose up` deploys a fully functional Fennath instance.
