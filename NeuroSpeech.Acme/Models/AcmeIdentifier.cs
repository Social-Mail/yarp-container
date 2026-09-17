using System.Text.Json.Serialization;

namespace NeuroSpeech.Acme.Models;

public class AcmeIdentifier
{
    [JsonPropertyName("type")]
    public string Type {get;set;}

    [JsonPropertyName("value")]
    public string Value {get;set;}
    
}
