using DnsClientX;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DotNetReverseProxy;

public static class DnsResolver
{
    public static async IAsyncEnumerable<string> ResolveAsync(string domain, DnsRecordType type)
    {
        var r = await ClientX.QueryDns(domain, type, DnsEndpoint.Cloudflare, typedRecords: true);
        foreach (var answer in r.TypedAnswers!)
        {
            switch (answer)
            {
                case ARecord a:
                    yield return a.Address.ToString();
                    break;
                case AAAARecord a:
                    yield return a.Address.ToString();
                    break;
                case TxtRecord txt:
                    
                    yield return UnescapeTxtRecord(txt.Text);
                    break;
                case MxRecord mx:
                    var any = r.Additional.Where((x) => x.Name == mx.Exchange);
                    var hasAdditional = false;
                    foreach (var x in any)
                    {
                        hasAdditional = true;
                        yield return x.Data;
                    }
                    if (!hasAdditional)
                    {
                        await foreach (var ip in ResolveAsync(mx.Exchange, DnsRecordType.A))
                        {
                            yield return ip;
                        }
                    }
                    break;
            }
        }
    }

    static string UnescapeTxtRecord(string rawRecord)
    {
        if (string.IsNullOrEmpty(rawRecord))
            return rawRecord;

        // 1. Remove leading and trailing double quotes if they wrap the record
        if (rawRecord.StartsWith("\"") && rawRecord.EndsWith("\"") && rawRecord.Length >= 2)
        {
            rawRecord = rawRecord.Substring(1, rawRecord.Length - 2);
        }

        // 2. Replace escaped internal quotes (\" -> ")
        return rawRecord.Replace("\\\"", "\"");
    }
}
