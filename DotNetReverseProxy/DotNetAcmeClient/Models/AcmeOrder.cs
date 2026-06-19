using System.Text.Json.Serialization;
using RetroCoreFit;

namespace DotNetAcmeClient.Models;

public class AcmeOrder: AcmeStatus
{



    [JsonPropertyName("identifiers")]
    public AcmeIdentifier[] Identifiers { get; set; }


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

    [Header("location")]
    public string url {get;set;}
}
