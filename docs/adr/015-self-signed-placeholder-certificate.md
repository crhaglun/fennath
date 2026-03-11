# ADR-015: Self-Signed Placeholder Certificate on Startup

## Status

Accepted

## Context

When the proxy container starts before the operator has provisioned a TLS certificate
(e.g., first deployment, or after a volume wipe), Kestrel's `ServerCertificateSelector`
had no certificate to return. This caused two problems:

1. **Log noise**: Every incoming TLS handshake threw an exception, flooding logs with
   unactionable stack traces.
2. **Startup complexity**: A 30-line polling loop in `Program.cs` blocked the application
   for up to 10 minutes waiting for the operator to provision a certificate. If the timeout
   was reached, the proxy exited with a critical error.

Additionally, `CertificateStore.ReloadFromDisk()` set `_certificate = null` before loading
the replacement, creating a race window where concurrent `GetCertificate()` calls (from
Kestrel's TLS handshake thread pool) could observe `null`.

## Decision

Make `CertificateStore._certificate` a non-nullable field by initializing it to a
self-signed placeholder certificate on construction:

- **Placeholder generation**: On startup, if no valid certificate exists on disk,
  `CertificateStore` generates a self-signed certificate for `*.{domain}` with a 1-day
  expiry using `CertificateRequest` from `System.Security.Cryptography`. The placeholder
  is never persisted to disk.

- **Non-nullable guarantee**: `GetCertificate()` always returns a usable `X509Certificate2`.
  Kestrel's `ServerCertificateSelector` never receives `null`, eliminating TLS handshake
  exceptions entirely.

- **Atomic swap on reload**: `ReloadFromDisk()` loads the new certificate first, then
  atomically swaps the reference. The field is never set to `null` at any point, closing
  the race window.

- **IsPlaceholder property**: Callers that need to distinguish between the self-signed
  placeholder and a real certificate (health checks, renewal service) use
  `CertificateStore.IsPlaceholder`.

- **Health check semantics**: `CertificateHealthCheck` reports `Degraded` when using
  the placeholder (operator hasn't provisioned yet) and `Healthy` when a real certificate
  is loaded. This gives orchestrators a clear signal without the blunt `Unhealthy` status.

- **No startup wait**: The polling loop in `Program.cs` is removed entirely. The proxy
  starts immediately, serves HTTPS with the self-signed placeholder (browsers will warn),
  and transitions seamlessly to the real certificate when the operator provisions it via
  the existing `CertificateFileWatcher`.

## Consequences

### Benefits

- **Zero log noise**: No TLS exceptions, no polling loop warnings. A single info-level
  log on startup.
- **Instant HTTPS availability**: The proxy accepts TLS connections immediately. Browsers
  show a certificate warning during the provisioning window (typically seconds to minutes
  on first deployment), but connections are encrypted.
- **Simpler startup**: `Program.cs` reduced from `StartAsync()` + 30-line wait loop +
  `WaitForShutdownAsync()` to a single `RunAsync()` call.
- **No race conditions**: The non-nullable `_certificate` field eliminates the null window
  that existed in `ReloadFromDisk()`.
- **Cleaner operator integration**: The renewal service uses `IsPlaceholder` to determine
  whether provisioning is needed, replacing the previous `GetExpiry() is null` check.

### Trade-offs

- **Self-signed cert visible to clients**: During the provisioning window, HTTPS clients
  see a self-signed certificate. For a homelab reverse proxy, this is acceptable — the
  window is short and only occurs on first deployment or volume recreation.
- **RSA key generation on startup**: Generating a 2048-bit RSA key adds ~50ms to cold
  startup. Negligible for a long-running proxy.

### ADR compatibility

- **ADR-007** (Zero-downtime rotation): Enhanced — `ServerCertificateSelector` now always
  has a certificate to return, and the atomic swap pattern is more robust.
- **ADR-010** (Cert persistence): Compatible — the placeholder is never persisted. Real
  certificates are still loaded from and saved to disk as before.
