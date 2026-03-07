# ADR-006: OpenTelemetry for Full Observability

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath is an internet-facing reverse proxy. Observability is critical for:

- Debugging routing issues and backend failures
- Monitoring certificate expiration and renewal health
- Tracking request latencies and error rates per route
- Detecting DNS update failures or public IP changes

Options considered:

| Approach | Pros | Cons |
|----------|------|------|
| Prometheus + Grafana (self-hosted) | Full control | Operational burden, more infra to manage |
| OpenTelemetry → Grafana Cloud | Managed backend, full telemetry pipeline | Dependency on Grafana Cloud |
| Custom logging only | Simplest | No structured metrics or traces |

The homelab already has a Grafana Cloud account with a publicly available OTLP endpoint.

## Decision

We will instrument Fennath with the **OpenTelemetry .NET SDK** and export all three
telemetry signals — **traces, metrics, and logs** — to **Grafana Cloud via OTLP (gRPC)**.

### Traces
- Per-request distributed traces: incoming request → routing → backend call → response.
- W3C TraceContext propagation to backends (so backend services can continue the trace).

### Metrics
- `fennath.requests.total` — counter by route and HTTP status code
- `fennath.request.duration` — histogram by route
- `fennath.backend.health` — gauge per backend (up=1, down=0)
- `fennath.cert.expiry_days` — gauge per certificate
- `fennath.dns.update.total` — counter for DNS record updates
- `fennath.ip.changes.total` — counter for public IP changes

### Logs
- Structured logs via `ILogger` → OTel log exporter.
- Covers: request logs, cert renewal events, DNS updates, config changes, errors.

## Consequences

**Positive:**
- Full observability from day one — no blind spots.
- Grafana Cloud is managed — no self-hosted Prometheus/Loki/Tempo to maintain.
- OTel is vendor-neutral — can switch to any OTLP-compatible backend later.
- The .NET OTel SDK is first-party supported by Microsoft and actively maintained.

**Negative:**
- Dependency on Grafana Cloud availability for viewing telemetry.
  Fennath itself is not affected if Grafana Cloud is down — telemetry just gets dropped.
- OTLP gRPC export adds a small overhead per request.
  Negligible for homelab traffic volumes.
- Grafana Cloud free tier has retention and volume limits.
  Acceptable for homelab scale.
