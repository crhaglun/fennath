# ADR-012: Testing Strategy — Integration-Heavy, Behavior-Focused

**Status:** Accepted  
**Date:** 2026-03-07

## Context

Fennath is primarily an integration project — it glues together YARP, Certes, Loopia XML-RPC,
Docker, and OpenTelemetry. Most "units" are thin wrappers or configuration mapping. The
interesting behavior lives at the boundaries: Does the proxy actually route traffic? Does the
ACME flow actually produce a valid certificate? Does a Docker container event actually register
a route?

### The change-detector trap

Traditional unit-test-heavy approaches produce tests like:

```csharp
// BAD: This test adds no confidence. It's a change detector.
[Fact]
public void ConfigLoader_ParsesSubdomain()
{
    var yaml = "routes:\n  - subdomain: grafana\n    backend: http://localhost:3000";
    var config = ConfigLoader.Parse(yaml);
    Assert.Equal("grafana", config.Routes[0].Subdomain);
    Assert.Equal("http://localhost:3000", config.Routes[0].Backend);
}
```

This test will "pass" even if the proxy is completely broken. It verifies that a YAML parser
can parse YAML — which is already tested by the YamlDotNet library. If you rename a property,
the test breaks even though behavior is unchanged. It's pure noise.

### What actually matters

For Fennath, confidence comes from answering these questions:

1. If I send an HTTPS request to `grafana.example.com`, does it arrive at my Grafana backend?
2. If my public IP changes, do DNS records update within the configured interval?
3. If I start a Docker container with `fennath.subdomain=myapp`, can I reach it through the
   proxy within seconds?
4. If a certificate expires, does renewal happen before it's too late?
5. If I change `fennath.yaml`, do routes update without a restart?

None of these can be answered by unit tests with mocks.

## Decision

### Testing pyramid (inverted for this project)

```
        ┌──────────────┐
        │  E2E / Smoke  │   Few — full Docker Compose, real traffic
        ├──────────────┤
        │  Integration  │   Most tests live here — real HTTP, real containers
        ├──────────────┤
        │  Behavioral   │   Decision logic with meaningful inputs/outputs
        │   Unit Tests  │
        ├──────────────┤
        │  (No mocks-   │   Avoid — tests that only verify mock interactions
        │   for-mocks)  │
        └──────────────┘
```

### Tier 1: Integration tests (primary focus)

These start real servers, make real HTTP calls, and assert on observable outcomes.

**Proxy routing tests** — using `WebApplicationFactory<T>` or `TestServer`:
```csharp
// GOOD: Proves routing actually works end-to-end through YARP.
[Fact]
public async Task Request_to_configured_subdomain_is_proxied_to_backend()
{
    // Start a simple backend on a random port
    using var backend = new TestBackend(response: "hello from grafana");

    // Start Fennath with a route pointing to that backend
    using var fennath = new FennathTestHost(routes: new[]
    {
        new RouteConfig("grafana", backend.Url)
    });

    // Make a request with the right Host header
    var response = await fennath.Client.GetAsync("/",
        headers: new { Host = "grafana.example.com" });

    Assert.Equal("hello from grafana", await response.Content.ReadAsStringAsync());
}
```

**Docker discovery tests** — using Testcontainers for .NET:
```csharp
// GOOD: Proves the full Docker event → route registration flow.
[Fact]
public async Task Container_with_fennath_labels_becomes_routable()
{
    using var fennath = new FennathTestHost(dockerDiscovery: true);

    // Start a real container with fennath labels
    var container = new ContainerBuilder()
        .WithImage("nginx:alpine")
        .WithLabel("fennath.enable", "true")
        .WithLabel("fennath.subdomain", "testapp")
        .WithLabel("fennath.port", "80")
        .Build();
    await container.StartAsync();

    // Wait for route to appear (event-driven, should be fast)
    await fennath.WaitForRoute("testapp", timeout: TimeSpan.FromSeconds(10));

    var response = await fennath.Client.GetAsync("/",
        headers: new { Host = "testapp.example.com" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Stop container, route should disappear
    await container.StopAsync();
    await fennath.WaitForRouteRemoval("testapp", timeout: TimeSpan.FromSeconds(10));
}
```

**Certificate selection tests** — real TLS handshakes:
```csharp
// GOOD: Proves that ServerCertificateSelector returns the right cert for SNI.
[Fact]
public async Task Wildcard_cert_is_served_for_any_subdomain()
{
    using var fennath = new FennathTestHost(
        wildcardCert: TestCerts.Wildcard("*.example.com"));

    var cert = await fennath.GetServerCertificate("anything.example.com");

    Assert.Contains("*.example.com", cert.Subject);
}

[Fact]
public async Task Subdomain_override_cert_takes_precedence()
{
    using var fennath = new FennathTestHost(
        wildcardCert: TestCerts.Wildcard("*.example.com"),
        subdomainCerts: new { ["api.example.com"] = TestCerts.Individual("api.example.com") });

    var cert = await fennath.GetServerCertificate("api.example.com");

    Assert.Contains("api.example.com", cert.Subject);
    Assert.DoesNotContain("*", cert.Subject);
}
```

### Tier 2: Behavioral unit tests (for decision logic)

These test non-trivial logic that has meaningful inputs and outputs — no mocks of collaborators,
just functions with arguments and return values.

**IP change detection logic:**
```csharp
// GOOD: Tests a real decision with multiple edge cases.
[Theory]
[InlineData(new[] { "1.2.3.4", "1.2.3.4", "1.2.3.4" }, "1.2.3.4")]  // all agree
[InlineData(new[] { "1.2.3.4", "1.2.3.4", null },       "1.2.3.4")]  // one fails, two agree
[InlineData(new[] { "1.2.3.4", "5.6.7.8", "1.2.3.4" },  "1.2.3.4")]  // majority wins
[InlineData(new[] { "1.2.3.4", "5.6.7.8", "9.0.1.2" },  null)]       // no consensus
[InlineData(new[] { null, null, null },                   null)]       // all fail
public void PublicIpResolver_ConsensusLogic(string?[] responses, string? expected)
{
    var result = PublicIpResolver.FindConsensus(responses);
    Assert.Equal(expected, result);
}
```

**Route conflict resolution:**
```csharp
// GOOD: Tests the merge logic that determines which route wins.
[Fact]
public void Static_route_wins_over_docker_route_for_same_subdomain()
{
    var staticRoutes = new[] { new Route("myapp", "http://static:8080") };
    var dockerRoutes = new[] { new Route("myapp", "http://docker:9090") };

    var merged = RouteAggregator.Merge(staticRoutes, dockerRoutes);

    Assert.Single(merged);
    Assert.Equal("http://static:8080", merged[0].Backend);
}
```

**Config validation:**
```csharp
// GOOD: Tests that invalid configs are rejected with clear errors,
// not that valid configs are parsed correctly (that's a change detector).
[Fact]
public void Config_without_domain_is_rejected()
{
    var yaml = "routes:\n  - subdomain: grafana\n    backend: http://localhost:3000";
    var result = ConfigLoader.TryParse(yaml, out var config, out var errors);
    Assert.False(result);
    Assert.Contains(errors, e => e.Contains("domain"));
}
```

### Tier 3: Contract tests (for external APIs)

**Loopia XML-RPC request format:**
Validate that the XML we generate matches Loopia's expected schema. Use recorded
request/response pairs (not a mock — a fixture):

```csharp
// GOOD: Ensures our XML-RPC serialization matches what Loopia actually expects.
[Fact]
public void AddZoneRecord_generates_correct_xmlrpc_request()
{
    var request = LoopiaDnsProvider.BuildAddZoneRecordRequest(
        domain: "example.com",
        subdomain: "grafana",
        record: new ARecord("1.2.3.4", ttl: 300));

    // Compare against a known-good XML request captured from Loopia's documentation
    var expected = File.ReadAllText("Fixtures/loopia-add-a-record.xml");
    Assert.Equal(NormalizeXml(expected), NormalizeXml(request));
}
```

**ACME flow against Let's Encrypt staging:**
A slow integration test (tagged, not run on every build) that exercises the full ACME flow
against Let's Encrypt's staging environment. This is the ultimate validation that the cert
pipeline works.

### What we explicitly avoid

| Anti-pattern | Why it's harmful |
|-------------|------------------|
| Mocking YARP internals | Proves nothing about routing behavior |
| Mocking `HttpClient` for IP detection | Proves nothing about real HTTP; test consensus logic directly |
| Asserting exact log messages | Pure change detector |
| Testing DI registration | Tests framework behavior, not ours |
| Coverage-target-driven tests | Incentivizes quantity over quality |
| Snapshot tests of config models | Break on any structural change, catch nothing |

### Test organization

```
tests/
└── Fennath.Tests/
    ├── Integration/
    │   ├── ProxyRoutingTests.cs           # Real HTTP through YARP
    │   ├── DockerDiscoveryTests.cs        # Testcontainers
    │   ├── CertificateSelectionTests.cs   # Real TLS handshakes
    │   ├── ConfigHotReloadTests.cs        # File change → route update
    │   └── AcmeStagingTests.cs            # Full ACME flow (slow, tagged)
    ├── Unit/
    │   ├── PublicIpConsensusTests.cs       # IP resolution logic
    │   ├── RouteConflictResolutionTests.cs # Merge/priority logic
    │   └── ConfigValidationTests.cs       # Invalid config rejection
    └── Contract/
        ├── Fixtures/                      # Recorded XML-RPC payloads
        └── LoopiaXmlRpcFormatTests.cs     # Request serialization
```

### Test infrastructure

- **`FennathTestHost`**: A reusable test fixture that starts a Fennath instance with
  configurable routes, certs, and discovery settings using `WebApplicationFactory<T>`.
- **`TestBackend`**: A minimal HTTP server that records requests and returns configured
  responses. Used as the backend for proxy routing tests.
- **`TestCerts`**: Helper to generate self-signed certificates for test use (wildcard,
  individual subdomain).
- **Testcontainers for .NET**: Manages Docker containers in integration tests. Requires
  Docker to be available on the test machine.

### When to write tests

Tests are written alongside the feature they validate — not after. Each phase's deliverables
include the corresponding tests. However, the test must assert on **behavior** (observable
outcome), not **implementation** (internal structure).

A good litmus test: "If I completely rewrite the internals but keep the same behavior, does
this test still pass?" If yes, it's a good test. If no, it's a change detector.

## Consequences

**Positive:**
- Tests provide genuine confidence that the system works.
- Integration tests catch real bugs (misconfigured YARP, TLS issues, race conditions).
- Refactoring is safe because tests assert on behavior, not implementation.
- No wasted effort on low-value tests.

**Negative:**
- Integration tests are slower than unit tests (~seconds vs. milliseconds).
  Acceptable for this project's size.
- Docker must be available to run the full test suite. Tests requiring Docker should be
  tagged so they can be skipped in environments without Docker.
- Fewer total tests than a coverage-driven approach. This is a feature, not a bug.
- Testcontainers adds a test dependency on Docker.DotNet and container image pulls.
