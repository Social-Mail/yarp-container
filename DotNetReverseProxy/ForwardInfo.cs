using System;
using System.Text.Json.Serialization;

namespace DotNetReverseProxy;

public class ForwardInfo {


    [JsonPropertyName("port")]
    public string Port { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; }

    public string Name{ get; set; }

    public string CacheKey => Name + ":" + Cert;

    [JsonPropertyName("cert")]
    public string Cert { get; set; }

    public static ForwardInfo FromJson(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ForwardInfo>(json)!;
        } catch (Exception ex) {
            throw new Exception("Failed to parse " + json, ex);
        }
    }

}
