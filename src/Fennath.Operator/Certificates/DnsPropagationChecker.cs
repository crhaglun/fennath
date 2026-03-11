using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Fennath.Operator.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Certificates;

/// <summary>
/// Queries public DNS resolvers to verify that an ACME challenge TXT record
/// has propagated before triggering validation. This avoids failures caused
/// by Let's Encrypt resolvers seeing stale cached values.
/// </summary>
public sealed partial class DnsPropagationChecker(
    IOptionsMonitor<OperatorConfig> OptionsMonitor,
    ILogger<DnsPropagationChecker> Logger)
{
    /// <summary>
    /// Polls public DNS resolvers until the expected TXT value is visible for
    /// the given FQDN, or the timeout expires.
    /// </summary>
    /// <returns>true if the value was found; false if the timeout expired.</returns>
    public async Task<bool> WaitForTxtRecordAsync(
        string fqdn, string expectedValue, CancellationToken ct = default)
    {
        var config = OptionsMonitor.CurrentValue.Certificates;
        var resolvers = config.DnsResolvers
            .Select(ip => new IPEndPoint(IPAddress.Parse(ip), 53))
            .ToArray();

        if (resolvers.Length == 0)
        {
            LogNoResolversConfigured(Logger);
            return true;
        }

        var client = new LookupClient(new LookupClientOptions(resolvers)
        {
            UseCache = false,
            Retries = 1,
            Timeout = TimeSpan.FromSeconds(5),
        });

        var timeout = TimeSpan.FromSeconds(config.DnsPropagationTimeoutSeconds);
        var interval = TimeSpan.FromSeconds(config.DnsPropagationPollingIntervalSeconds);
        var deadline = DateTimeOffset.UtcNow + timeout;

        LogWaitingForPropagation(Logger, fqdn, expectedValue[..Math.Min(16, expectedValue.Length)]);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await client.QueryAsync(fqdn, QueryType.TXT, cancellationToken: ct);
                var txtValues = result.Answers
                    .OfType<TxtRecord>()
                    .SelectMany(r => r.Text)
                    .ToList();

                if (txtValues.Contains(expectedValue))
                {
                    LogPropagationConfirmed(Logger, fqdn);
                    return true;
                }

                LogPropagationNotYet(Logger, fqdn, txtValues.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogResolverQueryFailed(Logger, fqdn, ex);
            }

            await Task.Delay(interval, ct);
        }

        LogPropagationTimeout(Logger, fqdn, (int)timeout.TotalSeconds);
        return false;
    }

    [LoggerMessage(EventId = 1130, Level = LogLevel.Information, Message = "Waiting for TXT record propagation: {fqdn} (expecting value starting with '{valuePrefix}...')")]
    private static partial void LogWaitingForPropagation(ILogger logger, string fqdn, string valuePrefix);

    [LoggerMessage(EventId = 1131, Level = LogLevel.Information, Message = "TXT record propagated and verified at public resolvers: {fqdn}")]
    private static partial void LogPropagationConfirmed(ILogger logger, string fqdn);

    [LoggerMessage(EventId = 1132, Level = LogLevel.Debug, Message = "TXT record not yet visible at public resolvers for {fqdn} ({txtCount} TXT records found)")]
    private static partial void LogPropagationNotYet(ILogger logger, string fqdn, int txtCount);

    [LoggerMessage(EventId = 1133, Level = LogLevel.Warning, Message = "DNS propagation timeout after {timeoutSeconds}s for {fqdn} — proceeding with ACME validation anyway")]
    private static partial void LogPropagationTimeout(ILogger logger, string fqdn, int timeoutSeconds);

    [LoggerMessage(EventId = 1134, Level = LogLevel.Warning, Message = "Failed to query public DNS resolvers for {fqdn}")]
    private static partial void LogResolverQueryFailed(ILogger logger, string fqdn, Exception ex);

    [LoggerMessage(EventId = 1135, Level = LogLevel.Warning, Message = "No DNS resolvers configured — skipping propagation check")]
    private static partial void LogNoResolversConfigured(ILogger logger);
}
