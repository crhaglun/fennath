using Fennath.Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fennath.Proxy;

/// <summary>
/// Reports unhealthy when no TLS certificate is loaded. This lets Docker
/// HEALTHCHECK (or any orchestrator) know the proxy isn't ready to serve
/// HTTPS traffic yet — e.g., during first-launch certificate provisioning.
/// </summary>
public sealed class CertificateHealthCheck(CertificateStore CertStore) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CertStore.GetCertificate() is not null
            ? HealthCheckResult.Healthy("TLS certificate loaded")
            : HealthCheckResult.Unhealthy("No TLS certificate — provisioning in progress"));
    }
}
