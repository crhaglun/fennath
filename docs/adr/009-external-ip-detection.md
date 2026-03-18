# ADR-009: External IP Detection via Polling

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath needs to detect when the host's public IP address changes so it can update DNS A records
via the Loopia API. Since the host is directly connected to the public internet (no NAT), the
local network interface IP *is* the public IP.

Three approaches were considered:

| Approach | Container-safe | Reliable | Complexity |
|----------|---------------|----------|------------|
| `NetworkChange.NetworkAddressChanged` event | ❌ No (sees Docker bridge IP) | ⚠️ Depends on ISP/DHCP behavior | Low |
| Poll external IP-echo service | ✅ Yes | ✅ Yes | Low |
| Subscribe to ISP/router API | ❌ ISP-specific | ⚠️ Varies | High |

### Why not `NetworkAddressChanged`?

- **Docker bridge mode:** The container has a virtual NIC (e.g., 172.17.x.x). When the host's
  public IP changes via ISP DHCP, the container's NIC address does not change. The event
  never fires.
- **Docker host mode (`--network=host`):** The container shares the host's NICs. The event
  *might* fire, but correctness now depends on a specific Docker network mode — a fragile
  assumption.
- **Bare metal (no container):** The event could fire when the ISP reassigns the IP, but
  behavior depends on whether the DHCP lease renewal triggers a NIC address change event
  or simply extends the existing lease. Unreliable.

## Decision

Fennath will detect public IP changes by **periodically polling external IP-echo services**
over HTTP. This is the same approach used by virtually all DynDNS clients.

### Implementation

- **Poll interval:** Configurable via `dns.publicIpCheckIntervalSeconds` (default: 300 seconds).
- **First-success with fallback:** Query services in order until one returns a valid IP address.
  If a service fails or returns non-IP content, try the next. If all services fail, throw an
  `AggregateException` so the caller can retain the last known IP and retry later.

  Default service list:
  ```
  https://api.ipify.org       — returns plain-text IPv4
  https://icanhazip.com       — returns plain-text IP
  https://checkip.amazonaws.com — returns plain-text IP
  ```

- **Change detection:** Compare the polling result against the last known IP. If changed:
  1. Update DNS A records via Loopia API
  2. Emit `fennath.ip.changes.total` OTel metric
  3. Log the old → new IP transition

- **Startup behavior:** On first start, always query and set DNS records (don't assume
  the current records are correct).

### Configuration

```yaml
dns:
  publicIpCheckIntervalSeconds: 300
  ipEchoServices:              # optional override
    - https://api.ipify.org
    - https://icanhazip.com
    - https://checkip.amazonaws.com
```

## Consequences

**Positive:**
- Works identically in Docker (any network mode) and on bare metal.
- Simple, proven approach — battle-tested by millions of DynDNS clients.
- Multiple services provide resilience against individual service outages.
- Configurable interval balances freshness vs. external request volume.

**Negative:**
- Depends on external services being available. If all three are down simultaneously,
  Fennath cannot detect IP changes until they recover. This is an unlikely scenario.
- Polling interval means there is a detection delay of up to `publicIpCheckIntervalSeconds`.
  For a homelab, a 5-minute delay between IP change and DNS update is acceptable.
- Each poll sends 2-3 HTTP requests to third-party services. At one poll per 5 minutes,
  this is ~860 requests/day — negligible.
