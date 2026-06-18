using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

public class AcmeChallenge: AcmeStatus
{

    [JsonPropertyName("type")]
    public string Type { get; set; }


    [JsonPropertyName("url")]
    public string url { get; set; }


    [JsonPropertyName("token")]
    public string Token { get; set; }


    [JsonPropertyName("keyAuthorization")]
    public string KeyAuthorization { get; set; }

    [JsonPropertyName("error")]
    public JsonNode Error {get;set;}
}
