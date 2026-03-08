using System.Threading.Channels;

namespace Fennath.Dns;

/// <summary>
/// Shared signal channel for DNS reconciliation. Producers (IpMonitorService,
/// DockerRouteDiscovery) write reasons; DnsReconciliationService reads them.
/// Bounded with DropOldest — bursts of signals (e.g., multiple container restarts)
/// collapse into a single reconciliation pass.
/// </summary>
public sealed class DnsReconciliationTrigger
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<string> Reader => _channel.Reader;

    public void Signal(string reason) => _channel.Writer.TryWrite(reason);
}
