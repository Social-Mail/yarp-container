using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetAcmeClient;



partial class AcmeClient
{

    public async Task<(string cert, string key)> CreateCertificateAsync(
        string domainName,
        string[] hostNames,
        Func<AcmeChallengeGroup[], CancellationToken,Task<IAsyncDisposable>> applyChallenges,
        CancellationToken cancellationToken = default
    )
    {
        // this will not save the certificate
        await this.InitializeAsync(cancellationToken);

        await this.EnsureAccountExistsAsync(cancellationToken);

        var order = await this.CreateOrderAsync(hostNames, cancellationToken);

        var authorizations = await GetAuthorizationChallengesAsync(order, cancellationToken);

        await using var d = await applyChallenges(authorizations, cancellationToken);

        bool authorizationSuccess = false;

        CancellationTokenSource done = new CancellationTokenSource();

        var list = new List<string>();

        for(int i = 0; i<10; i++) {

            await Task.Delay(TimeSpan.FromSeconds(5));

            await Task.WhenAll(authorizations.Select((a) =>
            {
                return Task.WhenAll(a.Challenges.Select(async (c) =>
                {
                    try {
                        await this.CompleteChallengeAsync(c.Uri, c.KeyAuthorization, done.Token);
                        authorizationSuccess = true;
                        done.Cancel();
                    } catch (Exception ex)
                    {
                        // do nothing...
                        if (ex is not TaskCanceledException)
                        {
                            list.Add(System.Text.Json.JsonSerializer.Serialize(new { error = ex.ToString() }));
                        }
                    }
                })); 
            }));
        }

        if (!authorizationSuccess)
        {
            var error = "[" + String.Join(",\n", list ) + "]";
            Console.WriteLine(error);
            throw new InvalidOperationException(error);
        }

        var csr = GenerateCsr(hostNames);

        var result = await this.FinalizeOrderAsync(order.Location, csr, cancellationToken);

        var cert = await this.DownloadCertificateAsync(result.Certificate, cancellationToken);

        var key = _accountKey.ExportRSAPrivateKeyPem();


        return (cert, key);

        async Task<AcmeChallengeGroup[]> GetAuthorizationChallengesAsync(AcmeOrder order, CancellationToken cancellationToken)
        {
            
            var all = await Task.WhenAll(order.Authorizations.Select(async (a) => {
                var auth = await this.GetAuthorizationAsync(a, cancellationToken);
                var challenges = await Task.WhenAll(auth.Challenges.Select((c) => this.GetChallengeAsync(c, cancellationToken)));
                return (auth, challenges);
            }));
            var grouped = all.SelectMany((x) => x.challenges.Select((c) => (c, x))).GroupBy((x) => x.c.Type, (x) => x.x);
            return grouped.Select((x) => new AcmeChallengeGroup( domainName, x.Key, x.First().auth, x.SelectMany((c) => c.challenges).ToArray())).ToArray();
        }

        string GenerateCsr(IEnumerable<string> domains, CancellationToken cancellationToken = default)
        {
            var subject = new X500DistinguishedName($"CN={domains.First()}");
            var request = new CertificateRequest(subject, _accountKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var domain in domains)
            {
                sanBuilder.AddDnsName(domain);
            }
            request.CertificateExtensions.Add(sanBuilder.Build());

            var certificate = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
            return Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
        }        
    }

}