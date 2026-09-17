using System.Text.Json.Serialization;

namespace NeuroSpeech.Acme.Models;

public class AcmeAccount
{
    [JsonPropertyName("location")]
    public string Location { get; set; }
    
    
    [JsonPropertyName("status")]
    public string Status { get; set; }


    [JsonPropertyName("contact")]
    public string[] Contact { get; set; }


    [JsonPropertyName("termsOfServiceAgreed")]
    public string TermsOfServiceAgreed { get; set; }
}
