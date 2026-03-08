using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Periodically sends a <see cref="DnsCommand.CleanStaleRecords"/> command
/// to trigger removal of DNS records for subdomains with no active container.
/// </summary>
public sealed class DnsReconciliationTimer(
    DnsCommandChannel Channel,
    IOptionsMonitor<FennathConfig> OptionsMonitor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = OptionsMonitor.CurrentValue.Dns.ReconciliationIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
            Channel.Send(new DnsCommand.CleanStaleRecords());
        }
    }
}
