using Fennath.Telemetry;

namespace Fennath.Operator.Dns;

/// <summary>
/// Pure executor — reads <see cref="DnsCommand"/> messages from the channel and
/// performs the corresponding DNS operation. Contains no detection logic.
/// Never deletes records — cleanup is a manual operation.
/// </summary>
public sealed partial class DnsReconciliationService(
    DnsCommandChannel Channel,
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
                using var activity = FennathMetrics.ActivitySource.StartActivity("fennath.dns-command");
                activity?.SetTag("command", command.GetType().Name);

                switch (command)
                {
                    case DnsCommand.IpChanged(var newIp):
                        await HandleIpChangedAsync(newIp, stoppingToken);
                        break;
                    case DnsCommand.SubdomainAdded(var subdomain):
                        await HandleSubdomainAddedAsync(subdomain, stoppingToken);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCommandFailed(Logger, command.GetType().Name, ex);

                // Re-queue the failed command so it will be retried after a brief delay.
                // The channel is bounded with DropOldest, so this won't block.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                Channel.Send(command);
            }
        }
    }

    private async Task HandleIpChangedAsync(string newIp, CancellationToken ct)
    {
        LogIpChangedUpdatingAll(Logger, newIp, _managedSubdomains.Count);

        foreach (var sub in _managedSubdomains)
        {
            await DnsProvider.UpsertARecordAsync(sub, newIp, ct: ct);
        }

        // Only record the IP after all updates succeed — if any threw,
        // the command will be re-queued and retried on the next attempt.
        _currentIp = newIp;
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

    [LoggerMessage(EventId = 1030, Level = LogLevel.Information, Message = "Creating A record for {subdomain} → {ip}")]
    private static partial void LogCreatingRecord(ILogger logger, string subdomain, string ip);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Information, Message = "IP changed to {ip}, updating {count} managed records")]
    private static partial void LogIpChangedUpdatingAll(ILogger logger, string ip, int count);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "DNS command {commandType} failed")]
    private static partial void LogCommandFailed(ILogger logger, string commandType, Exception ex);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Debug, Message = "Skipping A record for {subdomain} — no IP resolved yet")]
    private static partial void LogSkippingNoIp(ILogger logger, string subdomain);
}
