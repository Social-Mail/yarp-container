namespace DotNetAcmeClient;

public class AcmeOrder
{
    public string Status { get; set; }
    public string[] Identifiers { get; set; }
    public string NotBefore { get; set; }
    public string NotAfter { get; set; }
    public string Certificate { get; set; }
    public string[] Authorizations { get; set; }
    public string Finalize { get; set; }
    public string Location { get; set; }
}
