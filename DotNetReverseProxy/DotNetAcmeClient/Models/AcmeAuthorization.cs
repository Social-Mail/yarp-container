using System.Text.Json.Serialization;

namespace DotNetAcmeClient.Models;

public class AcmeAuthorization
{
    [JsonPropertyName("status")]
    public string Status { get; set; }


    [JsonPropertyName("identifier")]
    public AcmeIdentifier Identifier { get; set; }


    [JsonPropertyName("challenges")]
    public AcmeChallenge[] Challenges { get; set; }


    [JsonPropertyName("expires")]
    public string Expires { get; set; }

    public string url {get;set;}
}
