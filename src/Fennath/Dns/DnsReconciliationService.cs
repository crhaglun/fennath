using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Periodically reads shared state (current IP from <see cref="IpMonitorService"/>,
/// routes from <see cref="IRouteDiscovery"/> sources) and reconciles DNS A records.
///
/// Each poll cycle: creates missing records and updates all records if IP changed.
/// Full reconciliation (including stale record removal) runs on a longer interval.
/// </summary>
public sealed partial class DnsReconciliationService(
    IpMonitorService IpMonitor,
    IEnumerable<IRouteDiscovery> RouteSources,
    IDnsProvider DnsProvider,
    FennathMetrics Metrics,
    IOptionsMonitor<FennathConfig> OptionsMonitor,
    ILogger<DnsReconciliationService> Logger) : BackgroundService
{
    private readonly HashSet<string> _managedSubdomains = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastReconciledIp;
    private DateTime _lastFullReconciliation = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await ReconcileAsync(stoppingToken);
            var interval = OptionsMonitor.CurrentValue.Dns.ReconciliationPollIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        try
        {
            var currentIp = IpMonitor.CurrentIp;
            if (currentIp is null)
            {
                LogWaitingForIp(Logger);
                return;
            }

            var fullReconciliationDue = (DateTime.UtcNow - _lastFullReconciliation).TotalSeconds
                >= OptionsMonitor.CurrentValue.Dns.ReconciliationIntervalSeconds;

            if (fullReconciliationDue)
            {
                await ReconcileFullAsync(currentIp, ct);
                _lastFullReconciliation = DateTime.UtcNow;
            }
            else
            {
                await ReconcileAdditiveAsync(currentIp, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReconciliationFailed(Logger, ex);
        }
    }

    private async Task ReconcileAdditiveAsync(string currentIp, CancellationToken ct)
    {
        var ipChanged = currentIp != _lastReconciledIp;
        _lastReconciledIp = currentIp;

        var desiredSubdomains = GetDesiredSubdomains();
        var newSubdomains = desiredSubdomains.Except(_managedSubdomains, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var sub in newSubdomains)
        {
            LogCreatingRecord(Logger, sub, currentIp);
            await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
            _managedSubdomains.Add(sub);
            Metrics.DnsRecordsCreated.Add(1);
        }

        if (ipChanged && _managedSubdomains.Count > 0)
        {
            LogIpChangedUpdatingAll(Logger, currentIp, _managedSubdomains.Count);
            foreach (var sub in _managedSubdomains)
            {
                await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
            }

            Metrics.DnsUpdatesTotal.Add(1);
        }
    }

    private async Task ReconcileFullAsync(string currentIp, CancellationToken ct)
    {
        _lastReconciledIp = currentIp;

        var desiredSubdomains = GetDesiredSubdomains();
        var staleSubdomains = _managedSubdomains.Except(desiredSubdomains, StringComparer.OrdinalIgnoreCase).ToList();
        var newSubdomains = desiredSubdomains.Except(_managedSubdomains, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var sub in newSubdomains)
        {
            LogCreatingRecord(Logger, sub, currentIp);
            await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
            _managedSubdomains.Add(sub);
            Metrics.DnsRecordsCreated.Add(1);
        }

        foreach (var sub in staleSubdomains)
        {
            LogRemovingStaleRecord(Logger, sub);
            await DnsProvider.RemoveARecordAsync(sub, ct: ct);
            _managedSubdomains.Remove(sub);
            Metrics.DnsRecordsRemoved.Add(1);
        }

        foreach (var sub in _managedSubdomains)
        {
            await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
        }

        LogFullReconciliationComplete(Logger, _managedSubdomains.Count, newSubdomains.Count, staleSubdomains.Count);
    }

    private HashSet<string> GetDesiredSubdomains()
    {
        var subdomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "@" };

        foreach (var source in RouteSources)
        {
            foreach (var route in source.GetRoutes())
            {
                subdomains.Add(route.Subdomain);
            }
        }

        return subdomains;
    }

    [LoggerMessage(EventId = 1030, Level = LogLevel.Information, Message = "Creating A record for {subdomain} → {ip}")]
    private static partial void LogCreatingRecord(ILogger logger, string subdomain, string ip);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Information, Message = "IP changed to {ip}, updating {count} managed records")]
    private static partial void LogIpChangedUpdatingAll(ILogger logger, string ip, int count);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Information, Message = "Removing stale A record for {subdomain}")]
    private static partial void LogRemovingStaleRecord(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Information, Message = "Full reconciliation complete: {totalManaged} managed, {newCount} created, {staleCount} removed")]
    private static partial void LogFullReconciliationComplete(ILogger logger, int totalManaged, int newCount, int staleCount);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "DNS reconciliation failed, will retry next cycle")]
    private static partial void LogReconciliationFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Debug, Message = "Waiting for IP monitor to resolve public IP")]
    private static partial void LogWaitingForIp(ILogger logger);
}
