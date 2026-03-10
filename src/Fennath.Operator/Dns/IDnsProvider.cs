namespace Fennath.Operator.Dns;

/// <summary>
/// Abstraction for DNS provider operations.
/// Allows swapping registrars without changing the rest of the codebase.
/// </summary>
public interface IDnsProvider
{
    /// <summary>
    /// Creates or updates an A record for the given subdomain.
    /// </summary>
    Task UpsertARecordAsync(string subdomain, string ipAddress, int ttl = 300, CancellationToken ct = default);

    /// <summary>
    /// Removes an A record for the given subdomain.
    /// </summary>
    Task RemoveARecordAsync(string subdomain, CancellationToken ct = default);

    /// <summary>
    /// Creates a TXT record (used for ACME DNS-01 challenges).
    /// </summary>
    Task CreateTxtRecordAsync(string subdomain, string value, int ttl = 60, CancellationToken ct = default);

    /// <summary>
    /// Removes a TXT record for the given subdomain.
    /// </summary>
    Task RemoveTxtRecordAsync(string subdomain, CancellationToken ct = default);

    /// <summary>
    /// Gets all A records for the given subdomain.
    /// </summary>
    Task<IReadOnlyList<string>> GetARecordsAsync(string subdomain, CancellationToken ct = default);
}
