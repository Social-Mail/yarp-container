using System.Text.Json.Serialization;

namespace DotNetAcmeClient.Models;

public class AcmeStatus
{
    [JsonPropertyName("status")]
    public string Status {get;set;}
}
