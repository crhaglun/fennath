using Fennath.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Background service that periodically checks the public IP and updates
/// DNS A records (wildcard + root) via IDnsProvider when the IP changes.
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
        do
        {
            await UpdateDnsAsync(stoppingToken);
            var interval = OptionsMonitor.CurrentValue.Dns.PublicIpCheckIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
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

            if (_lastKnownIp is not null)
            {
                LogIpChanged(Logger, _lastKnownIp, currentIp);
                Metrics.IpChangesTotal.Add(1);
            }
            else
            {
                LogInitialIp(Logger, currentIp);
            }

            _lastKnownIp = currentIp;

            // Update wildcard record (*.domain)
            await DnsProvider.UpsertARecordAsync("*", currentIp, ct: ct);

            // Update root record (@)
            await DnsProvider.UpsertARecordAsync("@", currentIp, ct: ct);

            LogDnsUpdateComplete(Logger, 2);
            Metrics.DnsUpdatesTotal.Add(1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDnsUpdateFailed(Logger, ex);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Public IP unchanged: {ip}")]
    private static partial void LogIpUnchanged(ILogger logger, string ip);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Public IP changed from {previousIp} to {currentIp}")]
    private static partial void LogIpChanged(ILogger logger, string previousIp, string currentIp);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Initial public IP: {ip}")]
    private static partial void LogInitialIp(ILogger logger, string ip);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "DNS update complete, {count} records updated")]
    private static partial void LogDnsUpdateComplete(ILogger logger, int count);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "DNS update failed, will retry on next interval")]
    private static partial void LogDnsUpdateFailed(ILogger logger, Exception ex);
}
