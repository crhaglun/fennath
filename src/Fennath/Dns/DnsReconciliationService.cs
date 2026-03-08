using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Event-driven DNS reconciliation. Waits for signals from IpMonitorService or
/// DockerRouteDiscovery, then ensures A records match the desired state.
///
/// Fast path (signal-triggered): creates missing A records and updates IP on all
/// managed records. Never deletes — container restarts don't cause DNS outages.
///
/// Slow path (24h timeout): full reconciliation including removal of stale records
/// for subdomains that no longer have active containers.
/// </summary>
public sealed partial class DnsReconciliationService(
    PublicIpResolver IpResolver,
    IEnumerable<IRouteDiscovery> RouteSources,
    IDnsProvider DnsProvider,
    DnsReconciliationTrigger Trigger,
    FennathMetrics Metrics,
    IOptions<FennathConfig> Options,
    ILogger<DnsReconciliationService> Logger) : BackgroundService
{
    private static readonly TimeSpan FullReconciliationInterval = TimeSpan.FromHours(24);

    private readonly HashSet<string> _managedSubdomains = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentIp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial sync: ensure all current routes have A records
        await ReconcileAdditiveAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(FullReconciliationInterval);

            bool signaled;
            try
            {
                signaled = await Trigger.Reader.WaitToReadAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timeout — run full reconciliation including stale record cleanup
                await ReconcileFullAsync(stoppingToken);
                continue;
            }

            if (!signaled)
            {
                continue;
            }

            // Drain all pending signals
            while (Trigger.Reader.TryRead(out _)) { }

            await ReconcileAdditiveAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Fast path: ensure all desired A records exist. Never deletes.
    /// </summary>
    private async Task ReconcileAdditiveAsync(CancellationToken ct)
    {
        try
        {
            var currentIp = await IpResolver.GetPublicIpAsync(ct);
            var ipChanged = currentIp != _currentIp;
            _currentIp = currentIp;

            var desiredSubdomains = GetDesiredSubdomains();
            var newSubdomains = desiredSubdomains.Except(_managedSubdomains, StringComparer.OrdinalIgnoreCase).ToList();

            // Create records for new subdomains
            foreach (var sub in newSubdomains)
            {
                LogCreatingRecord(Logger, sub, currentIp);
                await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
                _managedSubdomains.Add(sub);
                Metrics.DnsRecordsCreated.Add(1);
            }

            // If IP changed, update all managed records
            if (ipChanged && _managedSubdomains.Count > 0)
            {
                LogIpChangedUpdatingAll(Logger, currentIp, _managedSubdomains.Count);
                foreach (var sub in _managedSubdomains)
                {
                    await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
                }

                Metrics.DnsUpdatesTotal.Add(1);
            }

            LogAdditiveReconciliationComplete(Logger, _managedSubdomains.Count, newSubdomains.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReconciliationFailed(Logger, ex);
        }
    }

    /// <summary>
    /// Slow path: full reconciliation including removal of stale records.
    /// </summary>
    private async Task ReconcileFullAsync(CancellationToken ct)
    {
        try
        {
            var currentIp = await IpResolver.GetPublicIpAsync(ct);
            _currentIp = currentIp;

            var desiredSubdomains = GetDesiredSubdomains();
            var staleSubdomains = _managedSubdomains.Except(desiredSubdomains, StringComparer.OrdinalIgnoreCase).ToList();
            var newSubdomains = desiredSubdomains.Except(_managedSubdomains, StringComparer.OrdinalIgnoreCase).ToList();

            // Create missing records
            foreach (var sub in newSubdomains)
            {
                LogCreatingRecord(Logger, sub, currentIp);
                await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
                _managedSubdomains.Add(sub);
                Metrics.DnsRecordsCreated.Add(1);
            }

            // Remove stale records
            foreach (var sub in staleSubdomains)
            {
                LogRemovingStaleRecord(Logger, sub);
                await DnsProvider.RemoveARecordAsync(sub, ct: ct);
                _managedSubdomains.Remove(sub);
                Metrics.DnsRecordsRemoved.Add(1);
            }

            // Ensure all records have current IP
            foreach (var sub in _managedSubdomains)
            {
                await DnsProvider.UpsertARecordAsync(sub, currentIp, ct: ct);
            }

            LogFullReconciliationComplete(Logger, _managedSubdomains.Count, newSubdomains.Count, staleSubdomains.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReconciliationFailed(Logger, ex);
        }
    }

    private HashSet<string> GetDesiredSubdomains()
    {
        var domain = Options.Value.Domain;
        var subdomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "@" };

        foreach (var source in RouteSources)
        {
            foreach (var route in source.GetRoutes())
            {
                // Map DiscoveredRoute subdomain to DNS subdomain
                // "@" stays as "@" (root), others are used as-is
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

    [LoggerMessage(EventId = 1033, Level = LogLevel.Debug, Message = "Additive reconciliation complete: {totalManaged} managed records, {newCount} created")]
    private static partial void LogAdditiveReconciliationComplete(ILogger logger, int totalManaged, int newCount);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Information, Message = "Full reconciliation complete: {totalManaged} managed, {newCount} created, {staleCount} removed")]
    private static partial void LogFullReconciliationComplete(ILogger logger, int totalManaged, int newCount, int staleCount);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "DNS reconciliation failed, will retry on next trigger")]
    private static partial void LogReconciliationFailed(ILogger logger, Exception ex);
}
