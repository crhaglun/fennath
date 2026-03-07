using System.Xml.Linq;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Loopia XML-RPC DNS provider. Implements IDnsProvider against
/// https://api.loopia.se/RPCSERV.
/// </summary>
public sealed partial class LoopiaDnsProvider(
    HttpClient httpClient,
    IOptions<FennathConfig> options,
    ILogger<LoopiaDnsProvider> logger) : IDnsProvider
{
    private const string ApiUrl = "https://api.loopia.se/RPCSERV";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _username = options.Value.Dns.Loopia.Username;
    private readonly string _password = options.Value.Dns.Loopia.Password;
    private readonly string _domain = options.Value.Domain;
    private readonly ILogger<LoopiaDnsProvider> _logger = logger;

    public async Task UpsertARecordAsync(string subdomain, string ipAddress, int ttl = 300, CancellationToken ct = default)
    {
        var existing = await GetZoneRecordsAsync(subdomain, ct);
        var aRecords = existing.Where(r => r.Type == "A").ToList();

        if (aRecords.Count > 0)
        {
            var match = aRecords.FirstOrDefault(r => r.Rdata == ipAddress);
            if (match is not null)
            {
                LogARecordAlreadyCurrent(_logger, subdomain, ipAddress);
                return;
            }

            // Remove stale A records, then add the new one
            foreach (var stale in aRecords)
            {
                await RemoveZoneRecordAsync(subdomain, stale.RecordId, ct);
            }
        }
        else
        {
            // Ensure subdomain exists
            await EnsureSubdomainAsync(subdomain, ct);
        }

        await AddZoneRecordAsync(subdomain, "A", ipAddress, ttl, 0, ct);
        LogARecordUpdated(_logger, subdomain, ipAddress);
    }

    public async Task RemoveARecordAsync(string subdomain, CancellationToken ct = default)
    {
        var records = await GetZoneRecordsAsync(subdomain, ct);

        foreach (var record in records.Where(r => r.Type == "A"))
        {
            await RemoveZoneRecordAsync(subdomain, record.RecordId, ct);
        }

        LogARecordRemoved(_logger, subdomain);
    }

    public async Task CreateTxtRecordAsync(string subdomain, string value, int ttl = 60, CancellationToken ct = default)
    {
        await EnsureSubdomainAsync(subdomain, ct);
        await AddZoneRecordAsync(subdomain, "TXT", value, ttl, 0, ct);
        LogTxtRecordCreated(_logger, subdomain);
    }

    public async Task RemoveTxtRecordAsync(string subdomain, CancellationToken ct = default)
    {
        var records = await GetZoneRecordsAsync(subdomain, ct);

        foreach (var record in records.Where(r => r.Type == "TXT"))
        {
            await RemoveZoneRecordAsync(subdomain, record.RecordId, ct);
        }

        LogTxtRecordRemoved(_logger, subdomain);
    }

    public async Task<IReadOnlyList<string>> GetARecordsAsync(string subdomain, CancellationToken ct = default)
    {
        var records = await GetZoneRecordsAsync(subdomain, ct);
        return records.Where(r => r.Type == "A").Select(r => r.Rdata).ToList();
    }

    // -- Loopia XML-RPC methods --

    private async Task EnsureSubdomainAsync(string subdomain, CancellationToken ct)
    {
        var response = await CallAsync("addSubdomain", [
            XmlRpcString(_username),
            XmlRpcString(_password),
            XmlRpcString(_domain),
            XmlRpcString(subdomain),
        ], ct);

        var status = ParseStringResponse(response);
        // "OK" or "DOMAIN_OCCUPIED" are both fine
        if (status is not ("OK" or "DOMAIN_OCCUPIED"))
        {
            throw new InvalidOperationException($"addSubdomain failed for '{subdomain}': {status}");
        }
    }

    private async Task AddZoneRecordAsync(
        string subdomain, string type, string rdata, int ttl, int priority, CancellationToken ct)
    {
        var recordStruct = XmlRpcStruct(
            ("type", XmlRpcString(type)),
            ("ttl", XmlRpcInt(ttl)),
            ("priority", XmlRpcInt(priority)),
            ("rdata", XmlRpcString(rdata)));

        var response = await CallAsync("addZoneRecord", [
            XmlRpcString(_username),
            XmlRpcString(_password),
            XmlRpcString(_domain),
            XmlRpcString(subdomain),
            recordStruct,
        ], ct);

        var status = ParseStringResponse(response);
        if (status != "OK")
        {
            throw new InvalidOperationException(
                $"addZoneRecord failed for '{subdomain}' ({type} {rdata}): {status}");
        }
    }

    private async Task RemoveZoneRecordAsync(string subdomain, int recordId, CancellationToken ct)
    {
        var response = await CallAsync("removeZoneRecord", [
            XmlRpcString(_username),
            XmlRpcString(_password),
            XmlRpcString(_domain),
            XmlRpcString(subdomain),
            XmlRpcInt(recordId),
        ], ct);

        var status = ParseStringResponse(response);
        if (status != "OK")
        {
            throw new InvalidOperationException(
                $"removeZoneRecord failed for '{subdomain}' record {recordId}: {status}");
        }
    }

    internal async Task<List<ZoneRecord>> GetZoneRecordsAsync(string subdomain, CancellationToken ct)
    {
        var response = await CallAsync("getZoneRecords", [
            XmlRpcString(_username),
            XmlRpcString(_password),
            XmlRpcString(_domain),
            XmlRpcString(subdomain),
        ], ct);

        return ParseZoneRecords(response);
    }

    // -- XML-RPC transport --

    private async Task<XDocument> CallAsync(string method, XElement[] parameters, CancellationToken ct)
    {
        var request = new XDocument(
            new XElement("methodCall",
                new XElement("methodName", method),
                new XElement("params",
                    parameters.Select(p => new XElement("param", new XElement("value", p))))));

        using var content = new StringContent(request.ToString(), System.Text.Encoding.UTF8, "text/xml");
        using var response = await _httpClient.PostAsync(ApiUrl, content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        return XDocument.Parse(body);
    }

    // -- XML-RPC value helpers --

    private static XElement XmlRpcString(string value) => new("string", value);
    private static XElement XmlRpcInt(int value) => new("int", value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static XElement XmlRpcStruct(params (string Name, XElement Value)[] members) =>
        new("struct",
            members.Select(m =>
                new XElement("member",
                    new XElement("name", m.Name),
                    new XElement("value", m.Value))));

    // -- Response parsing --

    private static string ParseStringResponse(XDocument doc)
    {
        return doc.Descendants("string").FirstOrDefault()?.Value
            ?? throw new InvalidOperationException("Unexpected XML-RPC response: no string value found.");
    }

    private static List<ZoneRecord> ParseZoneRecords(XDocument doc)
    {
        var records = new List<ZoneRecord>();

        foreach (var structEl in doc.Descendants("struct"))
        {
            var members = structEl.Elements("member")
                .ToDictionary(
                    m => m.Element("name")!.Value,
                    m => m.Element("value")!);

            if (!members.TryGetValue("type", out var typeEl))
                continue;

            records.Add(new ZoneRecord
            {
                Type = GetStringValue(typeEl),
                Ttl = GetIntValue(members.GetValueOrDefault("ttl")),
                Priority = GetIntValue(members.GetValueOrDefault("priority")),
                Rdata = GetStringValue(members.GetValueOrDefault("rdata")),
                RecordId = GetIntValue(members.GetValueOrDefault("record_id")),
            });
        }

        return records;
    }

    private static string GetStringValue(XElement? valueEl)
    {
        if (valueEl is null) return "";
        return valueEl.Element("string")?.Value ?? valueEl.Value;
    }

    private static int GetIntValue(XElement? valueEl)
    {
        if (valueEl is null) return 0;
        var text = valueEl.Element("int")?.Value ?? valueEl.Element("i4")?.Value ?? valueEl.Value;
        return int.TryParse(text, out var result) ? result : 0;
    }

    // -- Logging --

    [LoggerMessage(Level = LogLevel.Information, Message = "A record for '{subdomain}' already points to {ipAddress}")]
    private static partial void LogARecordAlreadyCurrent(ILogger logger, string subdomain, string ipAddress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated A record for '{subdomain}' to {ipAddress}")]
    private static partial void LogARecordUpdated(ILogger logger, string subdomain, string ipAddress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed A record(s) for '{subdomain}'")]
    private static partial void LogARecordRemoved(ILogger logger, string subdomain);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created TXT record for '{subdomain}'")]
    private static partial void LogTxtRecordCreated(ILogger logger, string subdomain);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed TXT record(s) for '{subdomain}'")]
    private static partial void LogTxtRecordRemoved(ILogger logger, string subdomain);
}

internal sealed class ZoneRecord
{
    public string Type { get; init; } = "";
    public int Ttl { get; init; }
    public int Priority { get; init; }
    public string Rdata { get; init; } = "";
    public int RecordId { get; init; }
}
