using System.Net;
using System.Text;
using Fennath.Operator.Configuration;
using Fennath.Operator.Dns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

public class LoopiaDnsProviderTests
{
    private static OperatorConfig CreateConfig(string domain = "example.com") => new()
    {
        Domain = domain,
        Dns = new DnsConfig
        {
            Loopia = new LoopiaConfig { Username = "testuser", Password = "testpass" }
        }
    };

    [Test]
    public async Task GetARecords_parses_zone_records_response()
    {
        var xml = """
            <?xml version="1.0"?>
            <methodResponse>
              <params>
                <param>
                  <value>
                    <array>
                      <data>
                        <value>
                          <struct>
                            <member><name>type</name><value><string>A</string></value></member>
                            <member><name>ttl</name><value><int>300</int></value></member>
                            <member><name>priority</name><value><int>0</int></value></member>
                            <member><name>rdata</name><value><string>1.2.3.4</string></value></member>
                            <member><name>record_id</name><value><int>42</int></value></member>
                          </struct>
                        </value>
                        <value>
                          <struct>
                            <member><name>type</name><value><string>TXT</string></value></member>
                            <member><name>ttl</name><value><int>60</int></value></member>
                            <member><name>priority</name><value><int>0</int></value></member>
                            <member><name>rdata</name><value><string>v=spf1</string></value></member>
                            <member><name>record_id</name><value><int>43</int></value></member>
                          </struct>
                        </value>
                      </data>
                    </array>
                  </value>
                </param>
              </params>
            </methodResponse>
            """;

        var provider = CreateProvider(xml);

        var aRecords = await provider.GetARecordsAsync("www");

        await Assert.That(aRecords).Count().IsEqualTo(1);
        await Assert.That(aRecords[0]).IsEqualTo("1.2.3.4");
    }

    [Test]
    public async Task UpsertARecord_sends_addZoneRecord_when_no_existing()
    {
        // First call: getZoneRecords returns empty, second: addSubdomain returns OK,
        // third: addZoneRecord returns OK
        var handler = new SequentialHandler([
            CreateXmlRpcResponse("<array><data></data></array>"),
            CreateXmlRpcStringResponse("OK"),
            CreateXmlRpcStringResponse("OK"),
        ]);

        var provider = CreateProvider(handler);

        await provider.UpsertARecordAsync("www", "5.6.7.8");

        await Assert.That(handler.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task UpsertARecord_skips_update_when_ip_already_matches()
    {
        var existingRecord = """
            <array><data>
              <value><struct>
                <member><name>type</name><value><string>A</string></value></member>
                <member><name>ttl</name><value><int>300</int></value></member>
                <member><name>priority</name><value><int>0</int></value></member>
                <member><name>rdata</name><value><string>5.6.7.8</string></value></member>
                <member><name>record_id</name><value><int>99</int></value></member>
              </struct></value>
            </data></array>
            """;

        var handler = new SequentialHandler([
            CreateXmlRpcResponse(existingRecord),
        ]);

        var provider = CreateProvider(handler);

        await provider.UpsertARecordAsync("www", "5.6.7.8");

        // Should only call getZoneRecords, not addZoneRecord
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateTxtRecord_calls_addSubdomain_then_addZoneRecord()
    {
        var handler = new SequentialHandler([
            CreateXmlRpcStringResponse("OK"),
            CreateXmlRpcStringResponse("OK"),
        ]);

        var provider = CreateProvider(handler);

        await provider.CreateTxtRecordAsync("_acme-challenge", "token123", 60);

        await Assert.That(handler.CallCount).IsEqualTo(2);
        await Assert.That(handler.RequestBodies[1]).Contains("token123");
    }

    [Test]
    public async Task RemoveTxtRecord_removes_only_txt_records()
    {
        var records = """
            <array><data>
              <value><struct>
                <member><name>type</name><value><string>A</string></value></member>
                <member><name>rdata</name><value><string>1.2.3.4</string></value></member>
                <member><name>record_id</name><value><int>10</int></value></member>
              </struct></value>
              <value><struct>
                <member><name>type</name><value><string>TXT</string></value></member>
                <member><name>rdata</name><value><string>token</string></value></member>
                <member><name>record_id</name><value><int>20</int></value></member>
              </struct></value>
            </data></array>
            """;

        var handler = new SequentialHandler([
            CreateXmlRpcResponse(records),
            CreateXmlRpcStringResponse("OK"),
        ]);

        var provider = CreateProvider(handler);

        await provider.RemoveTxtRecordAsync("_acme-challenge");

        // getZoneRecords + removeZoneRecord for TXT only (not A)
        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    // -- Helpers --

    private static LoopiaDnsProvider CreateProvider(string xmlResponse)
    {
        var handler = new FakeHandler(xmlResponse);
        return CreateProvider(handler);
    }

    private static LoopiaDnsProvider CreateProvider(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(CreateConfig());
        return new LoopiaDnsProvider(httpClient, options, NullLogger<LoopiaDnsProvider>.Instance);
    }

    private static string CreateXmlRpcResponse(string innerValue) =>
        $"""
        <?xml version="1.0"?>
        <methodResponse>
          <params><param><value>{innerValue}</value></param></params>
        </methodResponse>
        """;

    private static string CreateXmlRpcStringResponse(string value) =>
        CreateXmlRpcResponse($"<string>{value}</string>");

    private sealed class FakeHandler(string responseXml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
            });
    }

    private sealed class SequentialHandler(List<string> responses) : HttpMessageHandler
    {
        private int _index;

        public int CallCount => _index;
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(ct)
                : "";
            RequestBodies.Add(body);

            var xml = _index < responses.Count
                ? responses[_index]
                : CreateXmlRpcStringResponse("OK");
            _index++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "text/xml")
            };
        }
    }
}
