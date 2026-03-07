# ADR-008: No Authentication at Proxy Level

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Since Fennath is internet-facing (public IP, real domain, valid TLS), we considered whether
the proxy itself should enforce authentication before forwarding requests to backends.

Options:

| Approach | Complexity | Flexibility |
|----------|------------|-------------|
| No auth at proxy | Lowest | Backends fully control their own auth |
| Basic auth / API keys at proxy | Low | Coarse-grained, one mechanism for all |
| OAuth2/OIDC proxy (like Authelia) | High | Centralized SSO, rich policies |

## Decision

Fennath will **not perform any authentication or authorization**. It is a pure TLS-terminating
reverse proxy. Each backend service is responsible for its own authentication.

## Consequences

**Positive:**
- Simplest possible proxy — fewer moving parts, smaller attack surface in the proxy itself.
- Each backend can use the auth mechanism that suits it best (OAuth, API keys, mTLS, etc.).
- No centralized auth means no single point of failure for authentication.
- Fennath stays focused on one job: routing and TLS.

**Negative:**
- Every backend must implement its own auth — no "free" protection for services that
  lack authentication.
- No centralized audit log of who accessed what (each backend logs independently).
- Services that don't implement auth are exposed to the public internet with no protection.

**Future reconsideration:**
- If a centralized auth gateway becomes necessary, this could be added as an optional
  middleware layer in the YARP pipeline without changing the core proxy architecture.
  This ADR would then be superseded.
