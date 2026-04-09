using Fennath.Operator.Dns;
using Fennath.Tests.Helpers;

namespace Fennath.Tests.Integration;

public class DnsFlowTests : IAsyncDisposable
{
    private TestBackend _backend = null!;

    [Before(Test)]
    public async Task SetUp()
    {
        _backend = await TestBackend.CreateAsync();
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _backend.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _backend.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task IpChanged_command_upserts_A_record_for_apex()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("@"));
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));

        await WaitForAsync(() => ctx.DnsProvider.UpsertedARecords
            .Any(r => r.Subdomain == "@" && r.Ip == "1.2.3.4"));
    }

    [Test]
    public async Task SubdomainAdded_after_ip_known_creates_A_record()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        // Channel is FIFO — IpChanged is processed before SubdomainAdded,
        // so _currentIp is set when grafana is added.
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));

        await WaitForAsync(() => ctx.DnsProvider.UpsertedARecords
            .Any(r => r.Subdomain == "grafana" && r.Ip == "1.2.3.4"));
    }

    [Test]
    public async Task SubdomainAdded_before_ip_known_does_not_create_record()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));

        // Give the reconciliation service time to process (if it were going to act)
        await Task.Delay(200);

        var grafanaRecords = ctx.DnsProvider.UpsertedARecords
            .Where(r => r.Subdomain == "grafana")
            .ToList();

        await Assert.That(grafanaRecords).IsEmpty();
    }

    [Test]
    public async Task IpChanged_updates_all_managed_subdomains()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        // Register subdomains, then set IP so all get their initial A record
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("wiki"));
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));
        await WaitForAsync(() => ctx.DnsProvider.UpsertedARecords
            .Any(r => r.Subdomain == "wiki" && r.Ip == "1.2.3.4"));

        ctx.DnsProvider.UpsertedARecords.Clear();

        // IP changes — all managed subdomains should be updated
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("5.6.7.8"));

        await WaitForAsync(() =>
        {
            var updated = ctx.DnsProvider.UpsertedARecords.Select(r => r.Subdomain).ToHashSet();
            return updated.Contains("grafana") && updated.Contains("wiki");
        });
    }

    /// <summary>
    /// Polls a condition until it returns true or times out.
    /// Avoids flaky <c>Task.Delay</c>-based assertions.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException(
                    $"Condition was not met within {timeoutMs}ms.");
            }

            await Task.Delay(25);
        }
    }
}
