using System.Text.Json.Serialization;

namespace NeuroSpeech.Acme.Models;

public class AcmeStatus
{
    [JsonPropertyName("status")]
    public string Status {get;set;}
}
