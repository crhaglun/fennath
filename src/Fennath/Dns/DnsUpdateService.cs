using Fennath.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Background service that periodically checks the public IP and updates
/// DNS A records via IDnsProvider when the IP changes.
/// Also manages subdomain records for all configured routes.
/// </summary>
public sealed partial class DnsUpdateService(
    PublicIpResolver IpResolver,
    IDnsProvider DnsProvider,
    IOptionsMonitor<FennathConfig> OptionsMonitor,
    FennathMetrics Metrics,
    ILogger<DnsUpdateService> Logger) : BackgroundService
{
    private string? _lastKnownIp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial update on startup
        await UpdateDnsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = OptionsMonitor.CurrentValue.Dns.PublicIpCheckIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            await UpdateDnsAsync(stoppingToken);
        }
    }

    private async Task UpdateDnsAsync(CancellationToken ct)
    {
        try
        {
            var currentIp = await IpResolver.GetPublicIpAsync(ct);

            if (currentIp == _lastKnownIp)
            {
                LogIpUnchanged(Logger, currentIp);
                return;
            }

            var previousIp = _lastKnownIp;
            _lastKnownIp = currentIp;

            if (previousIp is not null)
            {
                LogIpChanged(Logger, previousIp, currentIp);
                Metrics.IpChangesTotal.Add(1);
            }
            else
            {
                LogInitialIp(Logger, currentIp);
            }

            var config = OptionsMonitor.CurrentValue;

            // Update wildcard record (*.domain)
            await DnsProvider.UpsertARecordAsync("*", currentIp, ct: ct);

            // Update root record (@)
            await DnsProvider.UpsertARecordAsync("@", currentIp, ct: ct);

            // Update each configured subdomain (skip @ since root is already updated above)
            foreach (var route in config.Routes.Where(
                r => !string.Equals(r.Subdomain, "@", StringComparison.Ordinal)))
            {
                await DnsProvider.UpsertARecordAsync(route.Subdomain, currentIp, ct: ct);
            }

            LogDnsUpdateComplete(Logger, config.Routes.Count + 2);
            Metrics.DnsUpdatesTotal.Add(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDnsUpdateFailed(Logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Public IP unchanged: {ip}")]
    private static partial void LogIpUnchanged(ILogger logger, string ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "Public IP changed from {previousIp} to {currentIp}")]
    private static partial void LogIpChanged(ILogger logger, string previousIp, string currentIp);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initial public IP: {ip}")]
    private static partial void LogInitialIp(ILogger logger, string ip);

    [LoggerMessage(Level = LogLevel.Information, Message = "DNS update complete, {count} records updated")]
    private static partial void LogDnsUpdateComplete(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "DNS update failed, will retry on next interval")]
    private static partial void LogDnsUpdateFailed(ILogger logger, Exception ex);
}
