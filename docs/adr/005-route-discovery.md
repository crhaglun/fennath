# ADR-005: Static Config + Docker Label Route Discovery

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath needs to know which subdomain maps to which backend service. This mapping can come
from several sources:

1. **Static configuration file** — a YAML file listing subdomain → backend mappings.
2. **Docker label auto-discovery** — read labels from running containers (like Traefik).
3. **Kubernetes Ingress resources** — watch K8s API for Ingress objects.
4. **Web UI / admin panel** — manage routes through a browser interface.

This is a single-box homelab deployment running Docker Compose. Kubernetes is out of scope
for now, and a web UI adds unnecessary complexity.

## Decision

We will support two route discovery mechanisms:

1. **Static YAML config (primary)** — always loaded, easy to debug, works without Docker.
   The file is watched for changes and hot-reloaded without restart.
2. **Docker label discovery (optional)** — watches the Docker socket for container
   start/stop events and reads `fennath.*` labels to auto-register routes.

Routes from both sources are merged. Docker-discovered routes are automatically removed
when the container stops. Static routes always take precedence in case of conflict
(same subdomain defined in both).

### Docker label convention

```
fennath.enable=true
fennath.subdomain=myapp
fennath.port=8080
fennath.healthcheck.path=/health
```

## Consequences

**Positive:**
- Static config is simple, version-controllable, and works without Docker.
- Docker discovery provides convenience — deploy a container with labels and it's
  automatically exposed through Fennath.
- Hot-reload means no downtime for config changes.
- Docker discovery is optional and disabled by default — no Docker dependency for basic use.

**Negative:**
- Docker discovery requires mounting the Docker socket (`/var/run/docker.sock`), which
  is a security-sensitive operation. The Fennath container has read-only access to all
  container metadata.
- Two route sources means potential conflicts. Resolution: static config wins, Docker
  routes for the same subdomain are ignored with a warning log.
- No web UI for now — all management is via files and Docker labels.

**Future extension:**
- Kubernetes Ingress discovery could be added as a third `IRouteDiscovery` implementation
  if the deployment ever moves to K8s.
