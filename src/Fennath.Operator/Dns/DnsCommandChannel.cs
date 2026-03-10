using System.Threading.Channels;

namespace Fennath.Operator.Dns;

/// <summary>
/// Commands that tell <see cref="DnsReconciliationService"/> exactly what to do.
/// Producers detect changes; the reconciler only executes.
/// </summary>
public abstract record DnsCommand
{
    /// <summary>Public IP changed — update all managed A records to the new IP.</summary>
    public sealed record IpChanged(string NewIp) : DnsCommand;

    /// <summary>A new subdomain appeared — create an A record for it.</summary>
    public sealed record SubdomainAdded(string Subdomain) : DnsCommand;
}

/// <summary>
/// Typed command channel between DNS producers and the reconciliation service.
/// </summary>
public sealed class DnsCommandChannel
{
    private readonly Channel<DnsCommand> _channel = Channel.CreateBounded<DnsCommand>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<DnsCommand> Reader => _channel.Reader;

    public void Send(DnsCommand command) => _channel.Writer.TryWrite(command);
}
