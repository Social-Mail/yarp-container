using System.Text.Json.Serialization;

namespace DotNetAcmeClient.Models;

public class AcmeCertificate
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; }
}
