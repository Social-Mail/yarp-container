using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

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
