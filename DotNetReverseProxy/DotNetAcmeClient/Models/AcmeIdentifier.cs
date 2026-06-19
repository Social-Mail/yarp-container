using System.Text.Json.Serialization;

namespace DotNetAcmeClient.Models;

public class AcmeIdentifier
{
    [JsonPropertyName("type")]
    public string Type {get;set;}

    [JsonPropertyName("value")]
    public string Value {get;set;}
    
}
