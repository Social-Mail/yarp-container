namespace DotNetAcmeClient;

// Data Models
public class AcmeDirectory
{
    public string NewAccount { get; set; }
    public string NewOrder { get; set; }
    public string NewNonce { get; set; }
    public string RevokeCert { get; set; }
    public string KeyChange { get; set; }
    public string Meta { get; set; }
}
