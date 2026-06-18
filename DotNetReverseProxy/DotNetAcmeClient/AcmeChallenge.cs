using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

public class AcmeChallenge
{

    [JsonPropertyName("type")]
    public string Type { get; set; }


    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; }


    [JsonPropertyName("token")]
    public string Token { get; set; }


    [JsonPropertyName("keyAuthorization")]
    public string KeyAuthorization { get; set; }
}
