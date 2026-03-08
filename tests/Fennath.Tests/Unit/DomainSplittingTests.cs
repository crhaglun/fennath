using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Dns;

namespace Fennath.Tests.Unit;

/// <summary>
/// Verifies that the domain/subdomain splitting logic correctly translates
/// between Fennath's logical subdomains and the registrar's (Loopia) view
/// of domain + subdomain.
/// </summary>
public class DomainSplittingTests
{
    // -- FennathConfig.EffectiveDomain --

    [Test]
    public async Task EffectiveDomain_WithSubdomain_CombinesSubdomainAndDomain()
    {
        var config = new FennathConfig { Domain = "my-domain.se", Subdomain = "lab" };
        await Assert.That(config.EffectiveDomain).IsEqualTo("lab.my-domain.se");
    }

    [Test]
    public async Task EffectiveDomain_WithoutSubdomain_ReturnsDomain()
    {
        var config = new FennathConfig { Domain = "my-domain.se" };
        await Assert.That(config.EffectiveDomain).IsEqualTo("my-domain.se");
    }

    [Test]
    public async Task EffectiveDomain_EmptySubdomain_ReturnsDomain()
    {
        var config = new FennathConfig { Domain = "my-domain.se", Subdomain = "" };
        await Assert.That(config.EffectiveDomain).IsEqualTo("my-domain.se");
    }

    // -- LoopiaDnsProvider.ToRegistrarSubdomain --

    [Test]
    public async Task ToRegistrarSubdomain_WithPrefix_ApexBecomesPrefix()
    {
        var result = LoopiaDnsProvider.ToRegistrarSubdomain("@", "lab");
        await Assert.That(result).IsEqualTo("lab");
    }

    [Test]
    public async Task ToRegistrarSubdomain_WithPrefix_ServiceSubdomainGetsPrefixAppended()
    {
        var result = LoopiaDnsProvider.ToRegistrarSubdomain("grafana", "lab");
        await Assert.That(result).IsEqualTo("grafana.lab");
    }

    [Test]
    public async Task ToRegistrarSubdomain_WithPrefix_AcmeChallengeGetsPrefixAppended()
    {
        var result = LoopiaDnsProvider.ToRegistrarSubdomain("_acme-challenge", "lab");
        await Assert.That(result).IsEqualTo("_acme-challenge.lab");
    }

    [Test]
    public async Task ToRegistrarSubdomain_NoPrefix_ApexPassesThrough()
    {
        var result = LoopiaDnsProvider.ToRegistrarSubdomain("@", "");
        await Assert.That(result).IsEqualTo("@");
    }

    [Test]
    public async Task ToRegistrarSubdomain_NoPrefix_ServiceSubdomainPassesThrough()
    {
        var result = LoopiaDnsProvider.ToRegistrarSubdomain("grafana", "");
        await Assert.That(result).IsEqualTo("grafana");
    }

    // -- AcmeService.ChallengeSubdomain (with EffectiveDomain) --

    [Test]
    public async Task ChallengeSubdomain_WithPrefix_StripsEffectiveDomain()
    {
        // ACME authorization for "lab.my-domain.se" → challenge at "_acme-challenge.lab.my-domain.se"
        // ChallengeSubdomain strips the effective domain, leaving "_acme-challenge"
        // LoopiaDnsProvider then translates "_acme-challenge" → "_acme-challenge.lab"
        var result = AcmeService.ChallengeSubdomain("lab.my-domain.se", "lab.my-domain.se");
        await Assert.That(result).IsEqualTo("_acme-challenge");
    }

    [Test]
    public async Task ChallengeSubdomain_WithoutPrefix_StripsRootDomain()
    {
        var result = AcmeService.ChallengeSubdomain("my-domain.se", "my-domain.se");
        await Assert.That(result).IsEqualTo("_acme-challenge");
    }
}
