# ADR-002: Certes as ACME Client Library

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath needs to automatically provision and renew TLS certificates from Let's Encrypt.
This requires an ACME v2 client library that supports:

- DNS-01 challenges (required for wildcard certificates)
- Account key management
- Certificate signing request (CSR) generation
- PFX/PEM export of obtained certificates

Libraries considered:

| Library | Status | Notes |
|---------|--------|-------|
| LettuceEncrypt | ❌ Archived April 2025 | Dead. Last updated for .NET 6. |
| LettuceEncrypt-Archon | ⚠️ Fork, maintained | HTTP-01 only, no DNS-01 support |
| Certes | ✅ Active | .NET Standard 2.0, full ACME v2, DNS-01 support |
| ACMESharpCore | ⚠️ Active | Sparse documentation |
| Kenc.ACMELib | ⚠️ Active | Smaller community |

## Decision

We will use **Certes** (`fszlin/certes` on GitHub, `Certes` on NuGet) as our ACME client library.

Certes targets .NET Standard 2.0, which is consumed by .NET 10 without issues. While not a
"modern .NET" library, certificate renewal is an infrequent operation (~once every 60 days) and
is not performance-sensitive. Pragmatism wins over purity here.

## Consequences

**Positive:**
- Certes has a clean, well-documented API for the full ACME lifecycle.
- Supports DNS-01 challenges natively, which we need for wildcard certs.
- Actively maintained with ACME v2 compliance.
- We own the DNS-01 challenge solver implementation (via Loopia API), so we are not
  dependent on Certes for provider-specific logic.

**Negative:**
- .NET Standard 2.0 target means no modern .NET APIs (Span<T>, etc.) in the library internals.
  This has zero practical impact since cert operations are infrequent and not in the hot path.
- If Certes is ever abandoned, we could swap to ACMESharpCore or a custom implementation
  since the ACME protocol is well-specified (RFC 8555).

**Alternatives rejected:**
- Writing a custom ACME client: ACME v2 has non-trivial JWS signing and nonce management.
  Not worth reimplementing when Certes works.
- Using an external tool (certbot, lego): Adds operational complexity and a non-.NET dependency.
  We prefer an in-process solution for simpler deployment.
