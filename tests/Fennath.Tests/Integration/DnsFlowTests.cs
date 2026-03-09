using Fennath.Sidecar.Dns;
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

        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));

        // Give DnsReconciliationService time to process the command
        await Task.Delay(200);

        // DnsReconciliationService always manages "@" (apex) — upsert for it
        await Assert.That(ctx.DnsProvider.UpsertedARecords)
            .Contains(("@", "1.2.3.4", 300));
    }

    [Test]
    public async Task SubdomainAdded_after_ip_known_creates_A_record()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        // First establish an IP
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));
        await Task.Delay(200);

        // Then add a subdomain
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));
        await Task.Delay(200);

        await Assert.That(ctx.DnsProvider.UpsertedARecords)
            .Contains(("grafana", "1.2.3.4", 300));
    }

    [Test]
    public async Task SubdomainAdded_before_ip_known_does_not_create_record()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        // Add subdomain before any IP is known
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));
        await Task.Delay(200);

        // No A record should be created for "grafana" since IP is unknown
        var grafanaRecords = ctx.DnsProvider.UpsertedARecords
            .Where(r => r.Subdomain == "grafana")
            .ToList();

        await Assert.That(grafanaRecords).IsEmpty();
    }

    [Test]
    public async Task IpChanged_updates_all_managed_subdomains()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        // Establish initial IP + register subdomains
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("1.2.3.4"));
        await Task.Delay(200);
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("grafana"));
        ctx.DnsChannel.Send(new DnsCommand.SubdomainAdded("wiki"));
        await Task.Delay(200);

        ctx.DnsProvider.UpsertedARecords.Clear();

        // IP changes — all managed subdomains should be updated
        ctx.DnsChannel.Send(new DnsCommand.IpChanged("5.6.7.8"));
        await Task.Delay(200);

        var updated = ctx.DnsProvider.UpsertedARecords.Select(r => r.Subdomain).ToHashSet();
        await Assert.That(updated).Contains("@");
        await Assert.That(updated).Contains("grafana");
        await Assert.That(updated).Contains("wiki");
    }
}
