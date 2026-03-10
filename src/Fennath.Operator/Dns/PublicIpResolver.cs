using System.Net;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Dns;

/// <summary>
/// Detects the public IP address by querying external echo services.
/// Tries multiple services for resilience (per ADR-009).
/// </summary>
public sealed partial class PublicIpResolver(
    HttpClient HttpClient,
    IOptions<FennathConfig> options,
    ILogger<PublicIpResolver> Logger)
{
    private readonly IReadOnlyList<string> EchoServices = options.Value.Dns.IpEchoServices;

    /// <summary>
    /// Queries echo services until one responds with a valid IP.
    /// Throws if all services fail.
    /// </summary>
    public async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        List<Exception>? failures = null;

        foreach (var service in EchoServices)
        {
            try
            {
                var ip = (await HttpClient.GetStringAsync(service, ct)).Trim();

                if (IPAddress.TryParse(ip, out _))
                {
                    LogIpResolved(Logger, ip, service);
                    return ip;
                }

                LogInvalidResponse(Logger, service, ip);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ex);
                LogServiceFailed(Logger, service, ex);
            }
        }

        throw new AggregateException(
            "All IP echo services failed. Check network connectivity.", failures ?? []);
    }

    [LoggerMessage(EventId = 1020, Level = LogLevel.Debug, Message = "Public IP resolved to {ip} via {service}")]
    private static partial void LogIpResolved(ILogger logger, string ip, string service);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Warning, Message = "IP echo service {service} returned invalid response: '{response}'")]
    private static partial void LogInvalidResponse(ILogger logger, string service, string response);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Warning, Message = "IP echo service {service} failed")]
    private static partial void LogServiceFailed(ILogger logger, string service, Exception ex);
}
