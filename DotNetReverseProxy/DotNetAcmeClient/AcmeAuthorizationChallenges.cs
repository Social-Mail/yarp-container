using System.Collections.Generic;

namespace DotNetAcmeClient;

public class AcmeChallengeGroup
{
    public readonly string Type;

    public readonly string DomainName;
    public readonly AcmeAuthorization Authorization;
    public readonly List<AcmeChallenge> Challenges = new List<AcmeChallenge>();

    public AcmeChallengeGroup(string domainName, string type, AcmeAuthorization authorization)
    {
        this.Type = type;
        this.DomainName = domainName;
        this.Authorization = authorization;
    }

}
