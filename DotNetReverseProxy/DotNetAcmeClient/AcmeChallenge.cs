namespace DotNetAcmeClient;

public class AcmeChallenge
{
    public string Type { get; set; }
    public string Status { get; set; }
    public string Uri { get; set; }
    public string Token { get; set; }
    public string KeyAuthorization { get; set; }
}
