# ADR-011: Dynamic Route Updates via YARP InMemoryConfigProvider

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath has two route sources (see ADR-005): static YAML config and Docker label discovery.
Both need the ability to add and remove routes at runtime without restarting the proxy.

Three questions drive this decision:

1. **How does YARP handle route changes at runtime?**
2. **How does Docker signal container start/stop events?**
3. **How does static config hot-reload work?**

## Decision

### YARP dynamic configuration

YARP provides `InMemoryConfigProvider`, which holds the current route and cluster configuration
in memory. Calling `Update(routes, clusters)` atomically swaps the entire routing table:

- In-flight requests complete using the configuration that was active when they started.
- New requests immediately use the updated configuration.
- No connections are dropped. No restart needed.

Fennath will use `InMemoryConfigProvider` as the single source of truth for YARP. Both route
discovery mechanisms feed into it.

### Route aggregation

A `RouteAggregator` service merges routes from all discovery sources:

```
StaticRouteDiscovery ──┐
                       ├──▶ RouteAggregator ──▶ YARP InMemoryConfigProvider
DockerRouteDiscovery ──┘
```

When any source reports a change, the aggregator:
1. Collects current routes from all sources
2. Resolves conflicts (static config wins; see ADR-005)
3. Calls `InMemoryConfigProvider.Update()` with the merged result

### Docker event stream

The Docker Engine API provides `GET /v1.43/events` — a long-lived HTTP connection that streams
real-time JSON events. No polling required. Events relevant to Fennath:

| Event | Action |
|-------|--------|
| `container.start` | Inspect container labels; if `fennath.enable=true`, register route |
| `container.stop` | Remove route for this container |
| `container.die` | Remove route for this container |

The .NET library **Docker.DotNet** (`Microsoft.Docker.DotNet` on NuGet) provides
`client.System.MonitorEventsAsync()` which wraps the event stream.

On startup, `DockerRouteDiscovery` also does a one-time `client.Containers.ListContainersAsync()`
to pick up containers that were already running before Fennath started.

### Static config file watching

`StaticRouteDiscovery` uses `FileSystemWatcher` on `fennath.yaml`. When the file changes:

1. Re-parse the YAML file
2. Validate the new configuration
3. If valid, push updated routes to the `RouteAggregator`
4. If invalid, log an error and keep the previous configuration

### Corresponding DNS updates

When routes are added or removed (from any source), Fennath also:
- Creates/removes DNS A records for the subdomain via the Loopia API (ADR-004)
- Requests individual TLS certificates if configured (ADR-003)

This means deploying a new Docker container with `fennath.subdomain=myapp` triggers the
full chain: Docker event → route registration → DNS A record creation → (optional) certificate
provisioning → traffic flows.

## Consequences

**Positive:**
- True zero-downtime route updates — no restart, no dropped connections.
- Docker label discovery is event-driven (not polled) — routes appear within seconds of
  container start.
- Static config changes are also picked up in near-real-time via file watcher.
- The `RouteAggregator` pattern cleanly separates route sources from YARP configuration.
- Adding a third route source in the future (e.g., Kubernetes Ingress) requires only a new
  `IRouteDiscovery` implementation — no changes to the aggregator or YARP layer.

**Negative:**
- Docker event stream is a long-lived connection. If the Docker daemon restarts, the stream
  breaks. `DockerRouteDiscovery` must reconnect and do a full container re-scan on reconnect.
- `FileSystemWatcher` can be unreliable on some filesystems (notably NFS). Since `fennath.yaml`
  is expected to be on a local filesystem (or Docker bind mount), this is acceptable.
- Multiple rapid changes (e.g., `docker compose up` starting 10 containers simultaneously)
  may cause multiple `Update()` calls in quick succession. YARP handles this correctly, but
  we should debounce to avoid unnecessary DNS API calls (e.g., 2-second debounce window).

**Initial implementation:**
Phase 1 will implement static config only. Docker discovery is Phase 4. The `RouteAggregator`
and `InMemoryConfigProvider` pattern will be established in Phase 1 so that adding Docker
discovery later is purely additive.
