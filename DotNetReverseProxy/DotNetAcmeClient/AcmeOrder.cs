using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

public class AcmeOrder
{
    [JsonPropertyName("status")]
    public string Status { get; set; }


    [JsonPropertyName("identifiers")]
    public string[] Identifiers { get; set; }


    [JsonPropertyName("notBefore")]
    public string NotBefore { get; set; }

    [JsonPropertyName("notAfter")]
    public string NotAfter { get; set; }

    [JsonPropertyName("certificate")]
    public string Certificate { get; set; }


    [JsonPropertyName("authorizations")]
    public string[] Authorizations { get; set; }

    [JsonPropertyName("finalize")]
    public string Finalize { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; }
}
