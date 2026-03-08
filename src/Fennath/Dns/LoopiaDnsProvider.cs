using System.Xml.Linq;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Dns;

/// <summary>
/// Loopia XML-RPC DNS provider. Implements IDnsProvider against
/// https://api.loopia.se/RPCSERV.
/// </summary>
public sealed partial class LoopiaDnsProvider(
    HttpClient HttpClient,
    IOptions<FennathConfig> options,
    ILogger<LoopiaDnsProvider> Logger) : IDnsProvider
{
    private const string ApiUrl = "https://api.loopia.se/RPCSERV";

    private readonly string Username = options.Value.Dns.Loopia.Username;
    private readonly string Password = options.Value.Dns.Loopia.Password;
    private readonly string Domain = options.Value.Domain;
    private readonly string Prefix = options.Value.Subdomain;

    /// <summary>
    /// Translates a logical subdomain (used by the rest of Fennath) into the
    /// subdomain string that the Loopia API expects, accounting for the optional
    /// domain prefix. Examples with prefix "lab":
    ///   "@"               → "lab"
    ///   "grafana"         → "grafana.lab"
    ///   "_acme-challenge" → "_acme-challenge.lab"
    /// With no prefix, values pass through unchanged.
    /// </summary>
    internal static string ToRegistrarSubdomain(string logicalSubdomain, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return logicalSubdomain;
        }

        if (logicalSubdomain == "@")
        {
            return prefix;
        }

        return $"{logicalSubdomain}.{prefix}";
    }

    public async Task UpsertARecordAsync(string subdomain, string ipAddress, int ttl = 300, CancellationToken ct = default)
    {
        var registrarSub = ToRegistrarSubdomain(subdomain, Prefix);
        var existing = await GetZoneRecordsAsync(registrarSub, ct);
        var aRecords = existing.Where(r => r.Type == "A").ToList();

        if (aRecords.Count > 0)
        {
            var match = aRecords.FirstOrDefault(r => r.Rdata == ipAddress);
            if (match is not null)
            {
                LogARecordAlreadyCurrent(Logger, subdomain, ipAddress);
                return;
            }

            // Remove stale A records, then add the new one
            foreach (var stale in aRecords)
            {
                await RemoveZoneRecordAsync(registrarSub, stale.RecordId, ct);
            }
        }
        else
        {
            // Ensure subdomain exists
            await EnsureSubdomainAsync(registrarSub, ct);
        }

        await AddZoneRecordAsync(registrarSub, "A", ipAddress, ttl, 0, ct);
        LogARecordUpdated(Logger, subdomain, ipAddress);
    }

    public async Task RemoveARecordAsync(string subdomain, CancellationToken ct = default)
    {
        var registrarSub = ToRegistrarSubdomain(subdomain, Prefix);
        var records = await GetZoneRecordsAsync(registrarSub, ct);

        foreach (var record in records.Where(r => r.Type == "A"))
        {
            await RemoveZoneRecordAsync(registrarSub, record.RecordId, ct);
        }

        LogARecordRemoved(Logger, subdomain);
    }

    public async Task CreateTxtRecordAsync(string subdomain, string value, int ttl = 60, CancellationToken ct = default)
    {
        var registrarSub = ToRegistrarSubdomain(subdomain, Prefix);
        await EnsureSubdomainAsync(registrarSub, ct);
        await AddZoneRecordAsync(registrarSub, "TXT", value, ttl, 0, ct);
        LogTxtRecordCreated(Logger, subdomain);
    }

    public async Task RemoveTxtRecordAsync(string subdomain, CancellationToken ct = default)
    {
        var registrarSub = ToRegistrarSubdomain(subdomain, Prefix);
        var records = await GetZoneRecordsAsync(registrarSub, ct);

        foreach (var record in records.Where(r => r.Type == "TXT"))
        {
            await RemoveZoneRecordAsync(registrarSub, record.RecordId, ct);
        }

        LogTxtRecordRemoved(Logger, subdomain);
    }

    public async Task<IReadOnlyList<string>> GetARecordsAsync(string subdomain, CancellationToken ct = default)
    {
        var registrarSub = ToRegistrarSubdomain(subdomain, Prefix);
        var records = await GetZoneRecordsAsync(registrarSub, ct);
        return records.Where(r => r.Type == "A").Select(r => r.Rdata).ToList();
    }

    // -- Loopia XML-RPC methods --

    private async Task EnsureSubdomainAsync(string subdomain, CancellationToken ct)
    {
        LogXmlRpcParams(Logger, "addSubdomain", Domain, subdomain);
        var response = await CallAsync("addSubdomain", [
            XmlRpcString(Username),
            XmlRpcString(Password),
            XmlRpcString(Domain),
            XmlRpcString(subdomain),
        ], ct);

        var status = ParseStringResponse(response);
        // "OK" or "DOMAIN_OCCUPIED" are both fine
        if (status is not ("OK" or "DOMAIN_OCCUPIED"))
        {
            LogXmlRpcResponseBody(Logger, "addSubdomain", response.ToString());
            throw new InvalidOperationException($"addSubdomain failed for '{subdomain}' (domain='{Domain}'): {status}");
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

        LogXmlRpcParams(Logger, "addZoneRecord", Domain, subdomain);
        var response = await CallAsync("addZoneRecord", [
            XmlRpcString(Username),
            XmlRpcString(Password),
            XmlRpcString(Domain),
            XmlRpcString(subdomain),
            recordStruct,
        ], ct);

        var status = ParseStringResponse(response);
        if (status != "OK")
        {
            LogXmlRpcResponseBody(Logger, "addZoneRecord", response.ToString());
            throw new InvalidOperationException(
                $"addZoneRecord failed for '{subdomain}' (domain='{Domain}', {type} {rdata}): {status}");
        }
    }

    private async Task RemoveZoneRecordAsync(string subdomain, int recordId, CancellationToken ct)
    {
        LogXmlRpcParams(Logger, "removeZoneRecord", Domain, subdomain);
        var response = await CallAsync("removeZoneRecord", [
            XmlRpcString(Username),
            XmlRpcString(Password),
            XmlRpcString(Domain),
            XmlRpcString(subdomain),
            XmlRpcInt(recordId),
        ], ct);

        var status = ParseStringResponse(response);
        if (status != "OK")
        {
            LogXmlRpcResponseBody(Logger, "removeZoneRecord", response.ToString());
            throw new InvalidOperationException(
                $"removeZoneRecord failed for '{subdomain}' (domain='{Domain}') record {recordId}: {status}");
        }
    }

    internal async Task<List<ZoneRecord>> GetZoneRecordsAsync(string subdomain, CancellationToken ct)
    {
        LogXmlRpcParams(Logger, "getZoneRecords", Domain, subdomain);
        var response = await CallAsync("getZoneRecords", [
            XmlRpcString(Username),
            XmlRpcString(Password),
            XmlRpcString(Domain),
            XmlRpcString(subdomain),
        ], ct);

        return ParseZoneRecords(response);
    }

    // -- XML-RPC transport --

    private async Task<XDocument> CallAsync(string method, XElement[] parameters, CancellationToken ct)
    {
        LogXmlRpcCall(Logger, method);

        var request = new XDocument(
            new XElement("methodCall",
                new XElement("methodName", method),
                new XElement("params",
                    parameters.Select(p => new XElement("param", new XElement("value", p))))));

        using var content = new StringContent(request.ToString(), System.Text.Encoding.UTF8, "text/xml");
        using var response = await HttpClient.PostAsync(ApiUrl, content, ct);
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

    /// <summary>
    /// Parses an XML-RPC response, detecting fault responses and extracting the status string.
    /// </summary>
    private static string ParseStringResponse(XDocument doc)
    {
        // Check for XML-RPC fault response first
        var fault = doc.Descendants("fault").FirstOrDefault();
        if (fault is not null)
        {
            var members = fault.Descendants("member")
                .ToDictionary(
                    m => m.Element("name")!.Value,
                    m => m.Element("value")!);

            var faultCode = members.TryGetValue("faultCode", out var codeEl)
                ? GetIntValue(codeEl) : -1;
            var faultString = members.TryGetValue("faultString", out var strEl)
                ? GetStringValue(strEl) : "unknown";

            return $"FAULT({faultCode}): {faultString}";
        }

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
            {
                continue;
            }

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
        if (valueEl is null)
        {
            return "";
        }

        return valueEl.Element("string")?.Value ?? valueEl.Value;
    }

    private static int GetIntValue(XElement? valueEl)
    {
        if (valueEl is null)
        {
            return 0;
        }

        var text = valueEl.Element("int")?.Value ?? valueEl.Element("i4")?.Value ?? valueEl.Value;
        return int.TryParse(text, out var result) ? result : 0;
    }

    // -- Logging --

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "A record for '{subdomain}' already points to {ipAddress}")]
    private static partial void LogARecordAlreadyCurrent(ILogger logger, string subdomain, string ipAddress);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Updated A record for '{subdomain}' to {ipAddress}")]
    private static partial void LogARecordUpdated(ILogger logger, string subdomain, string ipAddress);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "Removed A record(s) for '{subdomain}'")]
    private static partial void LogARecordRemoved(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Created TXT record for '{subdomain}'")]
    private static partial void LogTxtRecordCreated(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Information, Message = "Removed TXT record(s) for '{subdomain}'")]
    private static partial void LogTxtRecordRemoved(ILogger logger, string subdomain);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Debug, Message = "XML-RPC call: {method}")]
    private static partial void LogXmlRpcCall(ILogger logger, string method);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "XML-RPC {method}: domain='{domain}', subdomain='{subdomain}'")]
    private static partial void LogXmlRpcParams(ILogger logger, string method, string domain, string subdomain);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Warning, Message = "XML-RPC {method} failed — response body:\n{responseBody}")]
    private static partial void LogXmlRpcResponseBody(ILogger logger, string method, string responseBody);
}

internal sealed class ZoneRecord
{
    public string Type { get; init; } = "";
    public int Ttl { get; init; }
    public int Priority { get; init; }
    public string Rdata { get; init; } = "";
    public int RecordId { get; init; }
}
