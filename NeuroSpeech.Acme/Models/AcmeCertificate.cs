using System.Text.Json.Serialization;

namespace NeuroSpeech.Acme.Models;

public class AcmeCertificate
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; }
}
