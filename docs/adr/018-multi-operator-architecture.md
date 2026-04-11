# ADR-018: Multi-Operator Architecture

**Status:** Accepted  
**Date:** 2026-04-11  
**Amends:** [ADR-005](005-route-discovery.md), [ADR-007](007-cert-rotation-no-restart.md), [ADR-014](014-sidecar-credential-isolation.md)

## Context

Fennath was designed as a single-domain proxy: one operator discovers routes for one
domain and provisions one wildcard certificate. As the homelab grows, there is a need to
serve traffic for multiple domains (e.g., `lab.example.com` and `apps.example.org`) with
independent certificate and DNS management for each.

Three deployment models were evaluated:

1. **Multiple full instances** — each domain gets its own proxy + operator pair. Rejected
   because only one process can bind ports 80/443, requiring a front-facing load balancer
   that defeats the purpose of Fennath being the entry point.

2. **Multiple operators, one proxy** — each domain gets its own operator container that
   writes config and certs to a shared volume. A single proxy aggregates everything and
   handles SNI-based certificate selection. **Selected.**

3. **Single multi-domain operator** — one operator handles all domains. Rejected due to
   high internal complexity (per-domain loops, multi-DNS provider instances) with no
   isolation benefits.

## Decision

### Deployment model

Multiple operator containers share a single proxy container. Each operator is configured
for exactly one domain and writes to its own config file and certificate subdirectory on
the shared volume.

### Operator-side changes

**Docker container affinity via `fennath.domain` label:**

All operators poll the same Docker socket and see all labeled containers. The required
`fennath.domain` label associates a container with a specific operator by matching the
operator's `EffectiveDomain`. Containers without the label are ignored.

- `fennath.domain` must be set and must match the operator's `EffectiveDomain` for the
  container to be claimed.
- Containers missing the label are skipped by all operators.

**Domain-scoped route IDs:**

`ProxyConfigWriter.BuildYarpConfig()` now generates route and cluster IDs that include a
domain slug (e.g., `route-lab-example-com-grafana`). This prevents ID collisions when
multiple operators write separate YARP config files that the proxy merges.

**Per-operator config paths:**

Each operator instance is configured with a unique `YarpConfigPath` (e.g.,
`yarp-config-lab.json`). Certificate storage uses a shared directory (`/data/shared/certs/`).
No code changes are needed — these are already configurable.

### Proxy-side changes

**Auto-discovered YARP configuration:**

The proxy uses `DirectoryJsonConfigurationProvider` to auto-discover all
`yarp-config-*.json` files in `YarpConfigDirectory` (defaults to `/data/shared/`).
A `FileSystemWatcher` detects new, changed, and deleted files at runtime — adding a new
operator does not require a proxy restart. .NET configuration's key-path merge combines
route/cluster entries from all files into a single YARP configuration.

**Multi-cert SNI selection:**

`CertificateStore` now maintains a `ConcurrentDictionary<string, X509Certificate2>` keyed
by hostname/wildcard pattern. On startup (and reload), it scans the storage directory
recursively for `*.pfx` files and indexes each certificate by its Subject Alternative
Names (SANs) and Common Name (CN).

`GetCertificate(string? hostname)` performs SNI-based lookup:
1. Exact hostname match
2. Wildcard match (e.g., `*.lab.example.com` for `grafana.lab.example.com`)
3. Self-signed placeholder fallback

The parameterless `GetCertificate()` is preserved for backward compatibility.

**Directory-tree certificate watching:**

`CertificateFileWatcher` now watches the entire certificate storage directory
(`IncludeSubdirectories = true`, `*.pfx` filter) instead of a single wildcard.pfx file.
This detects new certificates from any operator subdirectory.

### Deployment example

```yaml
services:
  proxy:
    image: fennath-proxy
    ports: ["443:8443", "80:8080"]
    volumes:
      - fennath-shared:/data/shared:ro

  operator-lab:
    image: fennath-operator
    environment:
      - Fennath__Domain=example.com
      - Fennath__Subdomain=lab
      - Fennath__YarpConfigPath=/data/shared/yarp-config-lab.json
      - Fennath__Certificates__StoragePath=/data/shared/certs
    volumes:
      - fennath-shared:/data/shared
      - /var/run/docker.sock:/var/run/docker.sock:ro

  operator-apps:
    image: fennath-operator
    environment:
      - Fennath__Domain=example.org
      - Fennath__YarpConfigPath=/data/shared/yarp-config-apps.json
      - Fennath__Certificates__StoragePath=/data/shared/certs
    volumes:
      - fennath-shared:/data/shared
      - /var/run/docker.sock:/var/run/docker.sock:ro
```

Container domain affinity:
```yaml
services:
  grafana:
    labels:
      fennath.subdomain: grafana
      fennath.domain: lab.example.com  # → grafana.lab.example.com
  nextcloud:
    labels:
      fennath.subdomain: cloud
      fennath.domain: example.org      # → cloud.example.org
```

## Consequences

**Positive:**
- Each operator is independent: different domains, DNS providers, credentials. A bug in
  one domain's operator doesn't affect others.
- Scaling is "add another operator service to docker-compose".
- Proxy-side changes (multi-cert SNI, multi-file config) are prerequisites for future
  HTTP-01 on-demand certs regardless of operator topology.
- No new dependencies or external tools.

**Negative:**
- The `fennath.domain` label is required on all Docker containers — existing deployments
  must add this label to each service.
- The certificate store's SAN/CN parsing is a new code path that must handle malformed
  certificates gracefully.

**Future:**
- HTTP-01 on-demand certificates will build on the multi-cert store and SNI selection
  introduced here.
- Additional operators for non-Docker sources (Kubernetes, cloud VMs) can follow the
  same pattern: write a YARP config file + certs to the shared volume.
