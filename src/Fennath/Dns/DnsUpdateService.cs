using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Background service that periodically checks the public IP and updates
/// DNS A records via IDnsProvider when the IP changes.
/// Also manages subdomain records for all configured routes.
/// </summary>
public sealed partial class DnsUpdateService : BackgroundService
{
    private readonly PublicIpResolver _ipResolver;
    private readonly IDnsProvider _dnsProvider;
    private readonly IOptionsMonitor<FennathConfig> _optionsMonitor;
    private readonly ILogger<DnsUpdateService> _logger;
    private string? _lastKnownIp;

    public DnsUpdateService(
        PublicIpResolver ipResolver,
        IDnsProvider dnsProvider,
        IOptionsMonitor<FennathConfig> optionsMonitor,
        ILogger<DnsUpdateService> logger)
    {
        _ipResolver = ipResolver;
        _dnsProvider = dnsProvider;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial update on startup
        await UpdateDnsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = _optionsMonitor.CurrentValue.Dns.PublicIpCheckIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            await UpdateDnsAsync(stoppingToken);
        }
    }

    private async Task UpdateDnsAsync(CancellationToken ct)
    {
        try
        {
            var currentIp = await _ipResolver.GetPublicIpAsync(ct);

            if (currentIp == _lastKnownIp)
            {
                LogIpUnchanged(_logger, currentIp);
                return;
            }

            var previousIp = _lastKnownIp;
            _lastKnownIp = currentIp;

            if (previousIp is not null)
            {
                LogIpChanged(_logger, previousIp, currentIp);
            }
            else
            {
                LogInitialIp(_logger, currentIp);
            }

            var config = _optionsMonitor.CurrentValue;

            // Update wildcard record (*.domain)
            await _dnsProvider.UpsertARecordAsync("*", currentIp, ct: ct);

            // Update root record (@)
            await _dnsProvider.UpsertARecordAsync("@", currentIp, ct: ct);

            // Update each configured subdomain (skip @ since root is already updated above)
            foreach (var route in config.Routes.Where(
                r => !string.Equals(r.Subdomain, "@", StringComparison.Ordinal)))
            {
                await _dnsProvider.UpsertARecordAsync(route.Subdomain, currentIp, ct: ct);
            }

            LogDnsUpdateComplete(_logger, config.Routes.Count + 2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDnsUpdateFailed(_logger, ex);
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
