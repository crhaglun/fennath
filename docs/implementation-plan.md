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
| Config Format      | Standard .NET IConfiguration / Options pattern |
| Container Runtime  | Docker / Docker Compose                       |
| Observability      | OpenTelemetry SDK → Grafana Cloud (OTLP)      |
| Host OS            | Linux (directly on public internet)           |
| Test Framework     | TUnit                                         |

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
│  │  • Static config via IOptionsMonitor      │       │
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
│       ├── appsettings.json                   # Framework defaults (Logging)
│       ├── Configuration/
│       │   ├── FennathConfig.cs               # Strongly-typed Options model
│       │   └── FennathConfigValidator.cs      # IValidateOptions cross-field rules
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
│       │   ├── StaticRouteDiscovery.cs        # From IOptionsMonitor<FennathConfig>
│       │   └── DockerRouteDiscovery.cs        # From Docker labels + events
│       └── Telemetry/
│           └── TelemetrySetup.cs              # OTel SDK configuration
├── tests/
│   └── Fennath.Tests/
│       ├── Unit/
│       │   ├── ConfigValidationTests.cs
│       │   └── RouteConflictResolutionTests.cs
│       └── Integration/
│           └── ProxyRoutingTests.cs
├── docs/
│   ├── adr/                                   # Architecture Decision Records
│   └── implementation-plan.md                 # This file
├── appsettings.example.json                   # User config template
├── Directory.Build.props                      # Central build properties
├── Directory.Packages.props                   # Central package management
├── fennath.slnx                               # .NET solution
├── global.json                                # SDK version + test runner
└── README.md
```

## Configuration Schema

Configuration uses the standard .NET `IConfiguration` / `IOptions<T>` pattern.
The `"Fennath"` section in `appsettings.local.json` (or environment variables) is
bound to `FennathConfig` with `[Required]` DataAnnotations and `IValidateOptions`
for cross-field validation. See `appsettings.example.json` at the repo root.

```json
{
  "Fennath": {
    "Domain": "example.com",
    "Dns": {
      "Provider": "loopia",
      "Loopia": { "Username": "user@loopiaapi", "Password": "" },
      "PublicIpCheckIntervalSeconds": 300
    },
    "Certificates": {
      "Email": "admin@example.com",
      "Wildcard": true,
      "Staging": false
    },
    "Routes": [
      {
        "Subdomain": "grafana",
        "Backend": "http://localhost:3000",
        "HealthCheck": { "Path": "/api/health", "IntervalSeconds": 30 }
      }
    ],
    "Docker": { "Enabled": true, "SocketPath": "/var/run/docker.sock" },
    "Telemetry": {
      "Endpoint": "https://otlp-gateway-prod-xx.grafana.net/otlp",
      "Protocol": "grpc",
      "ServiceName": "fennath"
    },
    "Server": {
      "HttpsPort": 443,
      "HttpPort": 80,
      "HttpToHttpsRedirect": true
    }
  }
}
```

Sensitive values use environment variables: `Fennath__Dns__Loopia__Password=secret`.

## Implementation Phases

### Phase 1: Foundation ✅
**Goal:** A working HTTP reverse proxy with static config.

- [x] Project scaffolding — .NET 10 solution, Directory.Build.props, central package management
- [x] Configuration model — strongly-typed Options classes with DataAnnotations + IValidateOptions
- [x] IConfiguration / Options pattern with env var support (replaced YAML)
- [x] YARP configurator — translate routes → YARP RouteConfig/ClusterConfig
- [x] Route discovery abstraction + static config implementation with IOptionsMonitor hot-reload
- [x] Route aggregator with conflict resolution (static wins over docker)
- [x] Basic HTTP reverse proxy with static routes
- [x] Integration tests — real HTTP through YARP with TestServer
- [x] Unit tests — config validation, route conflict resolution
- [x] Backend health checks — YARP active health checks wired from config, `/healthz` self-health endpoint

**Deliverable:** `dotnet run` forwards HTTP requests to backends based on configuration.

### Phase 2: TLS & Certificates (in progress)
**Goal:** HTTPS with automatic Let's Encrypt wildcard cert.

- [x] Loopia XML-RPC API client (`LoopiaDnsProvider` behind `IDnsProvider`)
- [x] Certes integration — ACME DNS-01 challenge solver via `AcmeService`
- [x] Certificate storage — PFX on disk + in-memory `ConcurrentDictionary` (`CertificateStore`)
- [x] Kestrel `ServerCertificateSelector` integration — dynamic cert selection per SNI hostname
- [x] Background certificate renewal service (`CertificateRenewalService` — auto-renew 30 days before expiry)
- [x] All certificate services wired into DI via `YarpConfigurator`
- [ ] TLS termination working end-to-end (needs real Let's Encrypt test or integration test)

**Deliverable:** `https://grafana.example.com` works with a valid Let's Encrypt cert.

### Phase 3: DNS Management ✅
**Goal:** Automatic DNS record management.

- [x] Public IP detection via external echo services with fallback (PublicIpResolver)
- [x] Periodic IP check with change detection (DnsUpdateService background service)
- [x] Automatic A record updates via Loopia API when IP changes
- [x] Subdomain record management tied to route configuration

**Deliverable:** DNS records stay in sync with config and public IP automatically.

### Phase 4: Docker Discovery
**Goal:** Auto-register routes from Docker container labels.

- [ ] Docker socket client — list running containers on startup + subscribe to events
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
- [ ] Graceful shutdown (drain connections)
- [ ] Dockerfile (multi-stage build, minimal image)
- [ ] `docker-compose.yaml` for deployment
- [ ] README.md with setup guide

**Deliverable:** `docker compose up` deploys a fully functional Fennath instance.
