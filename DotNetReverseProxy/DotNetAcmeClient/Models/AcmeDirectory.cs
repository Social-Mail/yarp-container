using System.Text.Json.Serialization;

namespace DotNetAcmeClient.Models;

// Data Models
public class AcmeDirectory
{
    [JsonPropertyName("newAccount")]
    public string NewAccount { get; set; }

    [JsonPropertyName("newOrder")]
    public string NewOrder { get; set; }

    [JsonPropertyName("newNonce")]
    public string NewNonce { get; set; }

    [JsonPropertyName("revokeCert")]
    public string RevokeCert { get; set; }

    [JsonPropertyName("keyChange")]
    public string KeyChange { get; set; }

    [JsonPropertyName("meta")]
    public System.Text.Json.Nodes.JsonNode Meta { get; set; }
}
