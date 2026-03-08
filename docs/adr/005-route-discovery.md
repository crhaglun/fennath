# ADR-005: Docker Label Route Discovery

**Status:** Accepted (amended)  
**Date:** 2026-03-07

## Context

Fennath needs to know which subdomain maps to which backend service. This mapping can come
from several sources:

1. ~~Static configuration file~~ — removed for brevity; Docker labels are sufficient.
2. **Docker label auto-discovery** — read labels from running containers (like Traefik).
3. **Kubernetes Ingress resources** — watch K8s API for Ingress objects.
4. **Web UI / admin panel** — manage routes through a browser interface.

This is a single-box homelab deployment running Docker Compose. Kubernetes is out of scope
for now, and a web UI adds unnecessary complexity.

## Decision

We use a single route discovery mechanism:

**Docker label discovery** — polls the Docker socket for running containers and reads
`fennath.*` labels to register routes. Routes are automatically removed when the
container stops.

### Docker label convention

```
fennath.subdomain=myapp
fennath.port=8080
fennath.healthcheck.path=/health
```

## Consequences

**Positive:**
- Docker discovery provides convenience — deploy a container with labels and it's
  automatically exposed through Fennath.
- Hot-reload means no downtime for route changes.
- Single source of truth — no conflict resolution needed between multiple route sources.

**Negative:**
- Docker discovery requires mounting the Docker socket (`/var/run/docker.sock`), which
  is a security-sensitive operation. The Fennath container has read-only access to all
  container metadata.
- No web UI for now — all management is via Docker labels.

**Future extension:**
- Kubernetes Ingress discovery could be added as a second `IRouteDiscovery` implementation
  if the deployment ever moves to K8s.
