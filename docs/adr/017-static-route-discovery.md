# ADR-017: Static Route Discovery Alongside Docker

**Status:** Accepted  
**Date:** 2026-04-11  
**Amends:** [ADR-005](005-route-discovery.md)

## Context

Fennath's route discovery has been Docker-only (ADR-005): containers opt in with
`fennath.subdomain` labels and routes are automatically registered. However, not all
backend services run as Docker containers on the same host. Common examples:

- NAS appliances with web UIs (e.g., Synology DSM)
- Hypervisor management interfaces (e.g., Proxmox)
- Services running on VMs or other physical machines
- IoT devices or embedded systems with web interfaces

These services need to be reachable through Fennath but cannot be labeled via Docker.

## Decision

We add a **static route discovery** source that reads routes from the operator's
configuration. It implements the existing `IRouteDiscovery` interface and is registered
alongside `DockerRouteDiscovery` in the DI container.

### Configuration

Static routes are defined in the `Fennath:StaticRoutes` config section:

```json
{
  "Fennath": {
    "StaticRoutes": [
      { "Subdomain": "nas", "BackendUrl": "http://192.168.1.50:5000" },
      { "Subdomain": "pve", "BackendUrl": "https://192.168.1.10:8006" }
    ]
  }
}
```

Or via environment variables:

```
Fennath__StaticRoutes__0__Subdomain=nas
Fennath__StaticRoutes__0__BackendUrl=http://192.168.1.50:5000
```

### Conflict resolution

When a static route and a Docker-discovered route claim the same subdomain, **the static
route wins**. This is enforced by DI registration order (static before Docker) and the
existing first-wins merge in `ProxyConfigWriter.Merge()`. This policy makes intentional
operator-authored config authoritative over auto-discovered routes.

### Hot-reload

Static routes are loaded via `IOptionsMonitor<OperatorConfig>` and automatically reload
when the underlying configuration source changes (e.g., `appsettings.local.json` is
edited). Only actual route changes trigger a `RoutesChanged` event and YARP config
rewrite; unrelated config changes are ignored.

### Validation

Each static route entry is validated at load time:
- Subdomain must be non-empty (after trimming).
- Backend URL must be an absolute `http` or `https` URI.
- Duplicate subdomains (case-insensitive) are rejected, keeping the first.
- Invalid entries are logged as warnings and skipped; valid entries are still loaded.

## Consequences

**Positive:**
- Physical hosts and VMs can be routed through Fennath without Docker.
- Follows the existing `IRouteDiscovery` abstraction — no changes to `ProxyConfigWriter`
  or the YARP config generation pipeline.
- Hot-reload means no restarts needed when adding or removing static routes.
- DNS reconciliation automatically picks up static route subdomains (same `DnsCommand`
  channel as Docker routes).

**Negative:**
- Two sources of truth for routes — operators must be aware that static routes override
  Docker-discovered routes for the same subdomain.
- Static route backends are not health-checked by the discovery layer (YARP health checks
  can be configured separately if needed).

**Future:**
- Additional discovery sources (Kubernetes, file-based, API) can follow the same pattern.
