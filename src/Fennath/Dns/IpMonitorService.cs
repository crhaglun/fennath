using Fennath.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Background service that periodically checks the public IP and sends
/// <see cref="DnsCommand.IpChanged"/> when it changes.
/// </summary>
public sealed partial class IpMonitorService(
    PublicIpResolver IpResolver,
    DnsCommandChannel Channel,
    IOptionsMonitor<FennathConfig> OptionsMonitor,
    FennathMetrics Metrics,
    ILogger<IpMonitorService> Logger) : BackgroundService
{
    private string? _lastKnownIp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await CheckIpAsync(stoppingToken);
            var interval = OptionsMonitor.CurrentValue.Dns.PublicIpCheckIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
    }

    private async Task CheckIpAsync(CancellationToken ct)
    {
        using var activity = FennathMetrics.ActivitySource.StartActivity("fennath.ip-check");
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
            Channel.Send(new DnsCommand.IpChanged(currentIp));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogIpCheckFailed(Logger, ex);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Public IP unchanged: {ip}")]
    private static partial void LogIpUnchanged(ILogger logger, string ip);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Public IP changed from {previousIp} to {currentIp}")]
    private static partial void LogIpChanged(ILogger logger, string previousIp, string currentIp);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Initial public IP: {ip}")]
    private static partial void LogInitialIp(ILogger logger, string ip);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "IP check failed, will retry on next interval")]
    private static partial void LogIpCheckFailed(ILogger logger, Exception ex);
}
