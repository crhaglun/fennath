using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Detects the public IP address by querying external echo services.
/// Tries multiple services for resilience (per ADR-009).
/// </summary>
public sealed partial class PublicIpResolver(
    HttpClient httpClient,
    IOptions<FennathConfig> options,
    ILogger<PublicIpResolver> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IReadOnlyList<string> _echoServices = options.Value.Dns.IpEchoServices;
    private readonly ILogger<PublicIpResolver> _logger = logger;

    /// <summary>
    /// Queries echo services until one responds with a valid IP.
    /// Throws if all services fail.
    /// </summary>
    public async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        List<Exception>? failures = null;

        foreach (var service in _echoServices)
        {
            try
            {
                var ip = (await _httpClient.GetStringAsync(service, ct)).Trim();

                if (System.Net.IPAddress.TryParse(ip, out _))
                {
                    LogIpResolved(_logger, ip, service);
                    return ip;
                }

                LogInvalidResponse(_logger, service, ip);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ex);
                LogServiceFailed(_logger, service, ex);
            }
        }

        throw new AggregateException(
            "All IP echo services failed. Check network connectivity.", failures ?? []);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Public IP resolved to {ip} via {service}")]
    private static partial void LogIpResolved(ILogger logger, string ip, string service);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IP echo service {service} returned invalid response: '{response}'")]
    private static partial void LogInvalidResponse(ILogger logger, string service, string response);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IP echo service {service} failed")]
    private static partial void LogServiceFailed(ILogger logger, string service, Exception ex);
}
