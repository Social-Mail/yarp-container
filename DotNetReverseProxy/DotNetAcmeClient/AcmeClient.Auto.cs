using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using RetroCoreFit;

namespace DotNetAcmeClient;



partial class AcmeClient
{

    public async Task<AcmeOrder> RefreshOrder(string url, CancellationToken cancellationToken)
    {
        var request = await ApiRequest(url, (object)null, cancellationToken, true, false);
        return (await request.GetResponseAsync<AcmeOrder>(this._httpClient, cancellationToken))!;
    }

    public async Task<string> CreateCertificateAsync(
        RSA domainKey,
        string domainName,
        string[] hostNames,
        Func<AcmeChallengeGroup[], CancellationToken,Task<IAsyncDisposable>> applyChallenges,
        CancellationToken cancellationToken = default
    )
    {
        // this will not save the certificate
        await this.InitializeAsync("akash@nsmailer.in", cancellationToken);

        var order = await this.CreateOrderAsync(hostNames, cancellationToken);

        var hasWildcard = hostNames.Any((h) => h.StartsWith("*."));

        Console.WriteLine($"Order: {JsonSerializer.Serialize(order, jsonOptions)}");

        var authorizations = await GetAuthorizationChallengesAsync(hasWildcard, order, cancellationToken);

        Console.WriteLine($"Authorizations: {JsonSerializer.Serialize(authorizations, jsonOptions)}");

        await using var d = await applyChallenges(authorizations, cancellationToken);

        bool authorizationSuccess = false;

        var list = new List<string>();
        await Task.WhenAll(authorizations.Select(async (a) =>
        {
            await Task.WhenAll(a.Challenges.Select(async (c) =>
            {
                try
                {
                    await this.CompleteChallengeAsync(c.url, cancellationToken);
                    authorizationSuccess = true;

                    await WaitForValidChallengeAsync(c.url, cancellationToken);

                }  catch (Exception ex)
                    {
                        // do nothing...
                        if (ex is not TaskCanceledException)
                        {
                            list.Add(System.Text.Json.JsonSerializer.Serialize(new { error = ex.ToString() }));
                        }
                    }
            }));
        }));

        var orderReady = false;
        for(int i=0;i<30;i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));

            var o = await this.RefreshOrder(order.url, cancellationToken);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(o));
            if (Regex.IsMatch("valid|ready", o.Status, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            {
                orderReady = true;
                break;
            }
        }

        if(!orderReady)
        {
            
            if (!authorizationSuccess)
            {
                var error = "[" + String.Join(",\n", list ) + "]";
                Console.WriteLine(error);
                throw new InvalidOperationException(error);
            }
        }

        var csr = GenerateCsr(domainKey, hostNames);

        var result = await this.FinalizeOrderAsync(order.Finalize, csr, cancellationToken);

        var cert = await this.DownloadCertificateAsync(result.Certificate, cancellationToken);

        return cert;

        async Task WaitForValidChallengeAsync(string url, CancellationToken cancellationToken)
        {
            for(int i=0;i<30;i++) {
                var request = await ApiRequest(url, (object)null, cancellationToken, true, false);
                var c = await request.GetResponseAsync<AcmeChallenge>(_httpClient, cancellationToken);
                if (Regex.IsMatch("valid|ready", c.Status, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                {
                    return;
                }
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(c));
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

        }

        async Task<AcmeChallengeGroup[]> GetAuthorizationChallengesAsync(bool hasWildcard, AcmeOrder order, CancellationToken cancellationToken)
        {
            Dictionary<string, AcmeChallengeGroup> pairs = new ();
            foreach(var a in order.Authorizations){
                // Console.WriteLine($"Fetching Authorization; {a}");
                var auth = await this.GetAuthorizationAsync(a, cancellationToken);

                var jwk = this.GetJwk();
                var keysum = Signer.SHA256Base64Url(System.Text.Json.JsonSerializer.Serialize(jwk, jsonOptions));
                foreach(var c in auth.Challenges) {
                    var k = c.Type;
                    if(!pairs.TryGetValue(k, out var g))
                    {
                        g = new AcmeChallengeGroup(domainName, k, auth);
                        pairs.Add(k, g);
                    }

                    c.KeyAuthorization = $"{c.Token}.{keysum}";
                    if (c.Type == "dns-01")
                    {
                        c.KeyAuthorization = Signer.SHA256Base64Url(c.KeyAuthorization);
                        g.Challenges.Add(c);
                        continue;
                    }
                    if(hasWildcard) {
                        continue;
                    }
                    g.Challenges.Add(c);
                }
            }
            var list = new List<AcmeChallengeGroup>();
            foreach(var kvp in pairs)
            {
                if (kvp.Value.Challenges.Any()) {
                    list.Add(kvp.Value);
                }
            }
            return list.ToArray();
        }

        string GenerateCsr(RSA domainKey, IEnumerable<string> domains, CancellationToken cancellationToken = default)
        {
            var subject = new X500DistinguishedName($"CN={domains.First()}");
    
            // 2. Initialize a clean CertificateRequest using your DOMAIN key pair
            var request = new CertificateRequest(subject, domainKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            
            // 3. Populate your Subject Alternative Names (SANs)
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var domain in domains)
            {
                sanBuilder.AddDnsName(domain);
            }
            request.CertificateExtensions.Add(sanBuilder.Build());

            // 4. CRITICAL FIX: Create a raw PKCS#10 CSR byte block instead of a self-signed cert
            byte[] rawCsrDerBytes = request.CreateSigningRequest();

            // 5. Convert the raw ASN.1 DER bytes to a clean web-safe Base64Url string
            string base64UrlCsr = Convert.ToBase64String(rawCsrDerBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');

            return base64UrlCsr;
        }        
    }

}