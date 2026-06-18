using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

public class AcmeStatus
{
    [JsonPropertyName("status")]
    public string Status {get;set;}
}
