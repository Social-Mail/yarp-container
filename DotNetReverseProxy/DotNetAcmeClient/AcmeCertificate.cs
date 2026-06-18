using System.Text.Json.Serialization;

namespace DotNetAcmeClient;

public class AcmeCertificate
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; }
}
