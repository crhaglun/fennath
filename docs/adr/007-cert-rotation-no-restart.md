# ADR-007: Zero-Downtime Certificate Rotation via ServerCertificateSelector

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Let's Encrypt certificates expire after 90 days and must be renewed regularly. The renewal
process produces a new certificate file on disk. The question is: how does Kestrel pick up
the new certificate?

Options considered:

| Approach | Downtime | Complexity |
|----------|----------|------------|
| Restart process after renewal | Brief (seconds) | Lowest |
| Kestrel `ServerCertificateSelector` | None | Medium |
| External TLS termination (nginx/haproxy) | None | High (extra component) |

As of .NET 10, Kestrel does not have built-in certificate file watching. However, it does
provide the `ServerCertificateSelector` callback, which is invoked on every new TLS connection
and can return any `X509Certificate2`.

## Decision

We will use Kestrel's **`ServerCertificateSelector`** for zero-downtime certificate rotation.

The implementation pattern:

1. **In-memory certificate store**: A `ConcurrentDictionary<string, X509Certificate2>`
   holds the current certificates, keyed by hostname pattern (e.g., `*.example.com`,
   `api.example.com`).

2. **ServerCertificateSelector**: Registered on Kestrel's HTTPS options. On each incoming
   TLS handshake, it receives the SNI hostname and returns the matching certificate from
   the in-memory store. Lookup order:
   - Exact subdomain match (e.g., `api.example.com`)
   - Wildcard match (e.g., `*.example.com`)

3. **Background renewal service**: When a new certificate is obtained, it:
   - Saves the PFX to disk (persistence across restarts)
   - Atomically swaps the in-memory reference in the `ConcurrentDictionary`
   - New TLS connections immediately use the new certificate
   - Old connections continue using their already-negotiated cert until they close

## Consequences

**Positive:**
- Zero downtime during certificate renewal — no connection drops, no restart needed.
- The `ServerCertificateSelector` architecture will enable per-subdomain certificate overrides
  in the future (different cert for different SNI hostname) without structural changes.
- Simple implementation — no file watchers or external tools needed.

**Negative:**
- `ServerCertificateSelector` is called on every TLS handshake. The dictionary lookup is
  O(1) and the overhead is negligible, but it is technically more work than a static cert.
- The in-memory store must be kept in sync with on-disk state. If the process crashes between
  obtaining a cert and saving to disk, the cert is lost. Mitigation: save to disk first,
  then update in-memory.
- `X509Certificate2` objects hold unmanaged resources. Old certificates must be properly
  disposed after swap. We'll use a brief delay before disposing to avoid racing with
  in-flight handshakes.

## Current Limitations

- **Per-subdomain certificate overrides** are not yet implemented. The in-memory store
  currently holds only a single wildcard certificate. The `ServerCertificateSelector`
  architecture supports this as a future capability without structural changes.
