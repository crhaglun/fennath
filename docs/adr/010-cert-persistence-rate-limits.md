# ADR-010: Certificate Persistence and Let's Encrypt Rate Limit Mitigation

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath obtains TLS certificates from Let's Encrypt via the ACME protocol. The question is
whether certificates should be persisted across restarts, or re-requested each time.

### Let's Encrypt Rate Limits (Production)

| Limit | Value |
|-------|-------|
| Certificates per registered domain | 50 per week |
| Duplicate certificates (same names) | 5 per week |
| Failed validations | 5 per hour per account/hostname |
| New orders | 300 per 3 hours |
| Accounts per IP | 10 per 3 hours |

During active development, the service may restart many times per hour. Without persistence,
each restart would trigger a new certificate request, quickly exhausting rate limits —
especially the 5/week duplicate certificate limit and the 5/hour failed validation limit
(if ACME flows are being debugged).

### Let's Encrypt Staging

Let's Encrypt provides a staging environment with much higher limits (~30,000 certs/week)
and certificates signed by a fake root (not browser-trusted). This is designed for development
and testing.

## Decision

### 1. Always persist certificates to disk

Certificates are saved as PFX files to the configured `certificates.storagePath` (default:
`/data/certs`). On startup, Fennath:

1. Checks for existing certificates on disk
2. If a valid, non-expired certificate exists → load it into memory and use it
3. If no certificate exists, or it expires within 30 days → request a new one from Let's Encrypt
4. Never request a certificate that already exists and is valid on disk

The ACME account key is also persisted to disk for stable account identity across restarts.

### 2. Staging mode for development

The `certificates.staging` config option (default: `false`) switches between:

- **Production:** `https://acme-v02.api.letsencrypt.org/directory`
- **Staging:** `https://acme-staging-v02.api.letsencrypt.org/directory`

During development, always use staging. Staging certificates are not browser-trusted but are
functionally identical for testing the ACME flow. The startup log should prominently warn
when staging mode is active.

### 3. On-disk layout

```
/data/certs/
├── account.key                 # ACME account private key (PEM)
├── wildcard.example.com.pfx    # Wildcard certificate + private key
├── api.example.com.pfx         # Per-subdomain override cert (if any)
└── metadata.json               # Cert metadata (expiry dates, issuer, domains)
```

## Consequences

**Positive:**
- Restarts never trigger unnecessary certificate requests — rate limits are preserved.
- Development workflow is safe: staging mode + persistence means you can restart hundreds
  of times without hitting any limits.
- Certificates survive container recreation (Docker volume mount).
- `metadata.json` provides a quick way to check certificate status without parsing PFX files.

**Negative:**
- PFX files contain private keys — the storage directory must have restrictive permissions
  (`chmod 600` on files, `chmod 700` on directory). Fennath should enforce this on startup.
- If the storage volume is lost (e.g., Docker volume deleted), a new certificate request is
  triggered. This is by design — it's the recovery path.
- Persisted certificates from staging mode will not work if you switch to production mode
  (different CA). Fennath should detect this mismatch and re-request.
