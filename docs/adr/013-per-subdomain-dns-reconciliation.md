# ADR-013: Per-Subdomain DNS with Event-Driven Reconciliation

**Status:** Accepted  
**Date:** 2026-03-07

## Context

The previous DNS implementation used a wildcard A record (`*.domain`) to route all
subdomains to the proxy. While simple, wildcard DNS is not best practice:

1. Some registrars reject wildcard records.
2. Downstream clients and resolvers may ignore them.
3. They route *all* subdomains, including unintended ones, to the proxy.

Additionally, the previous `DnsUpdateService` only tracked IP changes — it had no
awareness of Docker container changes. New containers added via Docker labels would be
routable (via the wildcard) but without explicit DNS records.

## Decision

Replace the wildcard DNS approach with **explicit per-subdomain A records** managed by
an event-driven reconciliation system:

1. **`IpMonitorService`** — Polls external IP on a configured interval. Signals a
   shared trigger when the IP changes.
2. **`DnsReconciliationTrigger`** — A bounded `Channel<string>` shared between producers
   (IP monitor, Docker discovery) and the consumer (DNS reconciliation service).
3. **`DnsReconciliationService`** — Waits for signals or a 24-hour periodic fallback.
   - **Fast path (signal-triggered):** Creates A records for new subdomains, updates all
     records if IP changed. Never deletes — container restarts don't cause DNS outages.
   - **Slow path (24-hour timer):** Full reconciliation including removal of stale records
     for subdomains with no active containers.

`DockerRouteDiscovery` signals the trigger alongside its existing `RoutesChanged` event.

The root (`@`) A record is always managed alongside per-subdomain records.

## Consequences

**Positive:**
- Each subdomain has an explicit A record — reliable across all registrars and resolvers.
- DNS state tracks Docker container state through event-driven coordination.
- Stale record cleanup is conservative (daily), avoiding DNS outages during container restarts.
- Clear separation: IP monitoring, route discovery, and DNS reconciliation are independent
  components coordinated through a shared channel.

**Negative:**
- More DNS API calls than the wildcard approach (one per subdomain instead of one wildcard).
- Slight propagation delay for new subdomains (DNS TTL applies per-record).

**Supersedes:** The wildcard DNS aspect of the previous DnsUpdateService implementation.
ADR-004 (Loopia as DNS provider) remains valid — only the record management strategy changed.
