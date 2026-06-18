namespace DotNetAcmeClient;

public readonly struct AcmeChallengeGroup
{
    public readonly string Type;

    public readonly string DomainName;
    public readonly AcmeAuthorization Authorization;
    public readonly AcmeChallenge[] Challenges;

    public AcmeChallengeGroup(string domainName, string type, AcmeAuthorization authorization, AcmeChallenge[] challenges)
    {
        this.Type = type;
        this.DomainName = domainName;
        this.Authorization = authorization;
        this.Challenges = challenges;
    }

}
