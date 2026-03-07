# ADR-011: Dynamic Route Updates via YARP InMemoryConfigProvider

**Status:** Accepted (amended)  
**Date:** 2026-03-07

## Context

Fennath uses Docker label discovery (see ADR-005) to determine routes. Routes need the
ability to be added and removed at runtime without restarting the proxy.

## Decision

### YARP dynamic configuration

YARP provides `InMemoryConfigProvider`, which holds the current route and cluster configuration
in memory. Calling `Update(routes, clusters)` atomically swaps the entire routing table:

- In-flight requests complete using the configuration that was active when they started.
- New requests immediately use the updated configuration.
- No connections are dropped. No restart needed.

Fennath uses `InMemoryConfigProvider` as the single source of truth for YARP.

### Route aggregation

A `RouteAggregator` service merges routes from all discovery sources:

```
DockerRouteDiscovery ──▶ RouteAggregator ──▶ YARP InMemoryConfigProvider
```

When any source reports a change, the aggregator:
1. Collects current routes from all sources
2. Deduplicates by subdomain (first occurrence wins)
3. Calls `InMemoryConfigProvider.Update()` with the merged result

### Docker polling

`DockerRouteDiscovery` periodically polls `client.Containers.ListContainersAsync()` to
detect container changes and reads `fennath.*` labels to register routes.

### Corresponding DNS updates

When routes are added or removed, Fennath also:
- Creates/removes DNS A records for the subdomain via the Loopia API (ADR-004)
- Provisions wildcard TLS certificates (ADR-003)

This means deploying a new Docker container with `fennath.subdomain=myapp` triggers the
full chain: Docker poll → route registration → DNS A record creation → certificate
provisioning → traffic flows.

## Consequences

**Positive:**
- True zero-downtime route updates — no restart, no dropped connections.
- The `RouteAggregator` pattern cleanly separates route sources from YARP configuration.
- Adding a new route source in the future (e.g., Kubernetes Ingress) requires only a new
  `IRouteDiscovery` implementation — no changes to the aggregator or YARP layer.

**Negative:**
- Docker polling introduces a small delay (configurable interval) before routes appear.
- Multiple rapid changes (e.g., `docker compose up` starting 10 containers simultaneously)
  may cause multiple `Update()` calls in quick succession. YARP handles this correctly.
