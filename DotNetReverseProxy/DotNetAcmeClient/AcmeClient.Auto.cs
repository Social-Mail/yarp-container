using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetAcmeClient;



partial class AcmeClient
{

    public async Task<(string cert, string key)> GetOrCreateCertificateAsync(
        string[] hostNames,
        Func<AcmeChallenge[],Task> handleChallenges,
        Func<AcmeChallenge[],Task> disposeChallenges,
        CancellationToken cancellationToken = default
    )
    {
        // this will not save the certificate
        await this.InitializeAsync(cancellationToken);

        await this.EnsureAccountExistsAsync(cancellationToken);

        var order = await this.CreateOrderAsync(hostNames, cancellationToken);

        var authorizations = await this.GetAuthorizationAsync(order.Authorizations, cancellationToken);

        var challenges = await this.GetChallengeAsync(order.Authorizations)

        try
        {
            
        } finally
        {
            
        }

        return ("", "");
    }

}