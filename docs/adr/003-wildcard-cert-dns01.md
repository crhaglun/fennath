# ADR-003: Wildcard Certificate via DNS-01 Challenge

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath proxies multiple subdomains (e.g., `grafana.example.com`, `git.example.com`) and needs
TLS certificates for each. Two strategies exist:

1. **Per-subdomain certificates** via HTTP-01 challenge — simpler challenge mechanism, but
   requires one cert per subdomain and port 80 to be routable for each.
2. **Wildcard certificate** (`*.example.com`) via DNS-01 challenge — one cert covers all
   subdomains, but requires DNS API access to create TXT records.

From a security standpoint, per-subdomain certs provide better blast radius isolation (a leaked
key only compromises one subdomain). However, for a homelab deployment, the operational simplicity
of a single wildcard cert outweighs the theoretical risk.

## Decision

We will use a **wildcard certificate as the default**, obtained via **DNS-01 challenge** through
the Loopia XML-RPC API. Individual per-subdomain certificates can be configured as an override
for sensitive services.

The DNS-01 flow:
1. Certes requests a challenge from Let's Encrypt for `*.example.com`
2. Fennath creates a `_acme-challenge.example.com` TXT record via Loopia XML-RPC API
3. Let's Encrypt verifies the TXT record
4. Fennath receives the wildcard certificate
5. Fennath cleans up the TXT record

## Consequences

**Positive:**
- One certificate covers all current and future subdomains — no cert management per service.
- Adding a new subdomain requires zero certificate work.
- One renewal cycle instead of N (simpler, less API calls, less to monitor).
- Per-subdomain override available for services that need isolated certificates.

**Negative:**
- Requires DNS API access (Loopia XML-RPC) — see ADR-004.
- If the wildcard cert's private key leaks, all subdomains are compromised.
  Mitigation: the key lives only on the Fennath host, protected by filesystem permissions.
- DNS-01 challenge is more complex to implement than HTTP-01.
- DNS propagation delays can slow certificate issuance (typically 30-120 seconds for Loopia).
