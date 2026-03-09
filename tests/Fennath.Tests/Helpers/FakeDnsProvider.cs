using Fennath.Sidecar.Dns;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Recording test double for <see cref="IDnsProvider"/>.
/// All calls are recorded for assertion; no real DNS operations occur.
/// </summary>
public sealed class FakeDnsProvider : IDnsProvider
{
    public List<(string Subdomain, string Ip, int Ttl)> UpsertedARecords { get; } = [];
    public List<string> RemovedARecords { get; } = [];
    public List<(string Subdomain, string Value, int Ttl)> CreatedTxtRecords { get; } = [];
    public List<string> RemovedTxtRecords { get; } = [];

    private readonly Dictionary<string, List<string>> _aRecords = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertARecordAsync(string subdomain, string ipAddress, int ttl = 300, CancellationToken ct = default)
    {
        UpsertedARecords.Add((subdomain, ipAddress, ttl));
        _aRecords[subdomain] = [ipAddress];
        return Task.CompletedTask;
    }

    public Task RemoveARecordAsync(string subdomain, CancellationToken ct = default)
    {
        RemovedARecords.Add(subdomain);
        _aRecords.Remove(subdomain);
        return Task.CompletedTask;
    }

    public Task CreateTxtRecordAsync(string subdomain, string value, int ttl = 60, CancellationToken ct = default)
    {
        CreatedTxtRecords.Add((subdomain, value, ttl));
        return Task.CompletedTask;
    }

    public Task RemoveTxtRecordAsync(string subdomain, CancellationToken ct = default)
    {
        RemovedTxtRecords.Add(subdomain);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetARecordsAsync(string subdomain, CancellationToken ct = default)
    {
        IReadOnlyList<string> result = _aRecords.TryGetValue(subdomain, out var records) ? records : [];
        return Task.FromResult(result);
    }
}
