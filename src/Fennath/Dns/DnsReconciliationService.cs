using Fennath.Discovery;
using Fennath.Telemetry;

namespace Fennath.Dns;

/// <summary>
/// Pure executor — reads <see cref="DnsCommand"/> messages from the channel and
/// performs the corresponding DNS operation. Contains no detection logic.
/// </summary>
public sealed partial class DnsReconciliationService(
    DnsCommandChannel Channel,
    IEnumerable<IRouteDiscovery> RouteSources,
    IDnsProvider DnsProvider,
    FennathMetrics Metrics,
    ILogger<DnsReconciliationService> Logger) : BackgroundService
{
    private readonly HashSet<string> _managedSubdomains = new(StringComparer.OrdinalIgnoreCase) { "@" };
    private string? _currentIp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in Channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                switch (command)
                {
                    case DnsCommand.IpChanged(var newIp):
                        await HandleIpChangedAsync(newIp, stoppingToken);
                        break;
                    case DnsCommand.SubdomainAdded(var subdomain):
                        await HandleSubdomainAddedAsync(subdomain, stoppingToken);
                        break;
                    case DnsCommand.CleanStaleRecords:
                        await HandleCleanStaleRecordsAsync(stoppingToken);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCommandFailed(Logger, command.GetType().Name, ex);
            }
        }
    }

    private async Task HandleIpChangedAsync(string newIp, CancellationToken ct)
    {
        _currentIp = newIp;
        LogIpChangedUpdatingAll(Logger, newIp, _managedSubdomains.Count);

        foreach (var sub in _managedSubdomains)
        {
            await DnsProvider.UpsertARecordAsync(sub, newIp, ct: ct);
        }

        Metrics.DnsUpdatesTotal.Add(1);
    }

    private async Task HandleSubdomainAddedAsync(string subdomain, CancellationToken ct)
    {
        if (!_managedSubdomains.Add(subdomain))
        {
            return;
        }

        if (_currentIp is null)
        {
            LogSkippingNoIp(Logger, subdomain);
            return;
        }

        LogCreatingRecord(Logger, subdomain, _currentIp);
        await DnsProvider.UpsertARecordAsync(subdomain, _currentIp, ct: ct);
        Metrics.DnsRecordsCreated.Add(1);
    }

    private async Task HandleCleanStaleRecordsAsync(CancellationToken ct)
    {
        var activeSubdomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "@" };
        foreach (var source in RouteSources)
        {
            foreach (var route in source.GetRoutes())
            {
                activeSubdomains.Add(route.Subdomain);
            }
        }

        var stale = _managedSubdomains
            .Except(activeSubdomains, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var sub in stale)
        {
            LogRemovingStaleRecord(Logger, sub);
            await DnsProvider.RemoveARecordAsync(sub, ct: ct);
            _managedSubdomains.Remove(sub);
            Metrics.DnsRecordsRemoved.Add(1);
        }

        if (stale.Count > 0)
        {
            LogStaleCleanupComplete(Logger, stale.Count, _managedSubdomains.Count);
        }
    }

    [LoggerMessage(EventId = 1030, Level = LogLevel.Information, Message = "Creating A record for {subdomain} → {ip}")]
    private static partial void LogCreatingRecord(ILogger logger, string subdomain, string ip);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Information, Message = "IP changed to {ip}, updating {count} managed records")]
    private static partial void LogIpChangedUpdatingAll(ILogger logger, string ip, int count);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Information, Message = "Removing stale A record for {subdomain}")]
    private static partial void LogRemovingStaleRecord(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Information, Message = "Stale cleanup complete: {removedCount} removed, {remainingCount} remaining")]
    private static partial void LogStaleCleanupComplete(ILogger logger, int removedCount, int remainingCount);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "DNS command {commandType} failed")]
    private static partial void LogCommandFailed(ILogger logger, string commandType, Exception ex);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Debug, Message = "Skipping A record for {subdomain} — no IP resolved yet")]
    private static partial void LogSkippingNoIp(ILogger logger, string subdomain);
}
