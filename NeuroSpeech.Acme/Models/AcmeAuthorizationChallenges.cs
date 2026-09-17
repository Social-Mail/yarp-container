using System.Collections.Generic;

namespace NeuroSpeech.Acme.Models;

public class AcmeChallengeGroup
{
    public string Type {get;}

    public string DomainName  {get;}
    public AcmeAuthorization Authorization  {get;}
    public List<AcmeChallenge> Challenges  {get;}

    public AcmeChallengeGroup(string domainName, string type, AcmeAuthorization authorization)
    {
        Challenges = new ();
        Type = type;
        DomainName = domainName;
        Authorization = authorization;
    }

}
