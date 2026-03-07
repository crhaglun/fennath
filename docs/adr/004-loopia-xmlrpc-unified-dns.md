# ADR-004: Loopia XML-RPC API for All DNS Management

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath needs two DNS capabilities:

1. **Dynamic A record updates** — keep subdomain A records pointing to the host's public IP,
   which may change (home ISP with dynamic IP assignment).
2. **TXT record management** — create and clean up `_acme-challenge` TXT records for
   Let's Encrypt DNS-01 challenges.

The domain is registered with **Loopia** (Swedish registrar). Loopia offers two APIs:

| API | Protocol | Capabilities |
|-----|----------|-------------|
| DynDNS | HTTP GET (`/nic/update`) | Update A/AAAA records only |
| XML-RPC | XML-RPC over HTTPS | Full DNS zone management (A, AAAA, TXT, CNAME, MX, etc.) |

Originally we considered using DynDNS for IP updates and XML-RPC only for ACME challenges.

## Decision

We will use **only the Loopia XML-RPC API** for all DNS operations, replacing the DynDNS
protocol entirely.

The XML-RPC API is a superset — it can do everything DynDNS does and more. Using a single
API reduces complexity and the number of protocols/credentials to manage.

The Loopia DNS provider will be implemented behind an `IDnsProvider` interface to allow
swapping registrars in the future without changing the rest of the codebase.

## Consequences

**Positive:**
- Single API for all DNS operations — simpler code, one credential set.
- Full control over the DNS zone: can create/delete any record type.
- Enables automatic subdomain creation when new routes are configured.
- `IDnsProvider` abstraction makes it possible to support other registrars later.

**Negative:**
- Loopia-specific implementation — tied to their XML-RPC schema.
  Mitigated by the `IDnsProvider` interface.
- XML-RPC is a dated protocol (verbose XML payloads). Acceptable since DNS updates are
  infrequent (every few minutes at most).
- Requires Loopia API credentials (username + password), which must be securely stored.
  Will be provided via environment variables, not config files.

**Loopia XML-RPC operations used:**
- `addZoneRecord(domain, subdomain, record)` — create DNS records
- `updateZoneRecord(domain, subdomain, record)` — update existing records
- `removeZoneRecord(domain, subdomain, recordId)` — clean up ACME challenges
- `getZoneRecords(domain, subdomain)` — query current records
