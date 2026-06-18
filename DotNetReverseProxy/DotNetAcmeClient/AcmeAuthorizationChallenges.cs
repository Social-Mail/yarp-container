using System.Collections.Generic;

namespace DotNetAcmeClient;

public class AcmeChallengeGroup
{
    public string Type {get;}

    public string DomainName  {get;}
    public AcmeAuthorization Authorization  {get;}
    public List<AcmeChallenge> Challenges  {get;}

    public AcmeChallengeGroup(string domainName, string type, AcmeAuthorization authorization)
    {
        Challenges = new ();
        this.Type = type;
        this.DomainName = domainName;
        this.Authorization = authorization;
    }

}
