namespace DotNetAcmeClient;

public class AcmeAuthorization
{
    public string Status { get; set; }
    public string Identifier { get; set; }
    public string[] Challenges { get; set; }
    public string Expires { get; set; }
}
