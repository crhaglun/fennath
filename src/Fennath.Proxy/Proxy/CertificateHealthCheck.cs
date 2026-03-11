using Fennath.Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fennath.Proxy;

/// <summary>
/// Reports certificate status so orchestrators know whether the proxy
/// is fully ready to serve HTTPS traffic.
/// Healthy: real certificate loaded.
/// Degraded: self-signed placeholder (operator hasn't provisioned yet).
/// </summary>
public sealed class CertificateHealthCheck(CertificateStore CertStore) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CertStore.IsPlaceholder
            ? HealthCheckResult.Degraded("Using self-signed placeholder — waiting for operator to provision certificate")
            : HealthCheckResult.Healthy("TLS certificate loaded"));
    }
}
