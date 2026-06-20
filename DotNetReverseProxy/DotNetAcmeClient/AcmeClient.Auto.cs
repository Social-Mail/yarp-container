using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DotNetAcmeClient.Models;
using RetroCoreFit;

namespace DotNetAcmeClient;



partial class AcmeClient
{

    public async Task<AcmeOrder> RefreshOrder(string url, CancellationToken cancellationToken)
    {
        var request = await ApiRequest(url, (object?)null, cancellationToken, true, false);
        return (await request.AsJsonAsync<AcmeOrder>(this._httpClient, cancellationToken))!;
    }

    private void Log<T>(T item)
    {
        var log = System.Text.Json.JsonSerializer.Serialize(item);
        Console.WriteLine(log);
    }

    public async Task<string> CreateCertificateAsync(
        string emailAddress,
        RSA domainKey,
        string hostName,
        Func<AcmeChallengeGroup[], CancellationToken,Task<IAsyncDisposable>> applyChallenges,
        CancellationToken cancellationToken = default
    )
    {
        // this will not save the certificate
        await this.InitializeAsync(emailAddress, cancellationToken);

        var hostNames = new[] { hostName };  

        var order = await this.CreateOrderAsync(hostNames, cancellationToken);

        var hasWildcard = hostName.StartsWith("*.");

        var authorizations = await GetAuthorizationChallengesAsync(hasWildcard, order, cancellationToken);

        await using var d = await applyChallenges(authorizations, cancellationToken);

        bool authorizationSuccess = false;

        var done = new CancellationTokenSource();

        var doneToken = done.Token;

        var list = new ConcurrentBag<object?>();
        await Task.WhenAll(authorizations.Select(async (a) =>
        {
            await Task.WhenAll(a.Challenges.Select(async (c) =>
            {
                try
                {
                    await this.CompleteChallengeAsync(c.url, doneToken);

                    if (doneToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var (result, error) = await WaitForValidChallengeAsync(c.url, cancellationToken);

                    if (error == null)
                    {
                        done.Cancel();
                        authorizationSuccess = true;
                    } else
                    {
                        list.Add(result);
                    }

                }  catch (Exception ex)
                {
                    // do nothing...
                    if (ex is TaskCanceledException)
                    {
                        return;
                    }
                    list.Add(new { error = ex.ToString() });
                }
            }));
        }));

        if (!authorizationSuccess)
        {
            throw new InvalidOperationException(System.Text.Json.JsonSerializer.Serialize(new { errors = list.ToArray()}));
        }

        var csr = GenerateCsr(domainKey, hostNames);

        await this.FinalizeOrderAsync(order.Finalize, csr, cancellationToken);

        var (result, error) = await WaitForValidChallengeAsync(order.url, cancellationToken);

        if(error != null) {
            throw new InvalidOperationException(System.Text.Json.JsonSerializer.Serialize(new { error }));
        }

        var certificate = (result!["certificate"] as JsonValue)!.ToString();

        var cert = await this.DownloadCertificateAsync(certificate, cancellationToken);

        return cert.Certificate;

        async Task<(System.Text.Json.Nodes.JsonObject? result, string? error)> WaitForValidChallengeAsync(
            string url,
            CancellationToken cancellationToken)
        {
            JsonObject? c = null;

            for(int i=0;i<30;i++) {
                var request = await ApiRequest(url, (object?)null, cancellationToken, true, false);
                c = await request.AsJsonAsync<System.Text.Json.Nodes.JsonObject>(_httpClient, cancellationToken);
                var status = (c["status"] as JsonValue)!.ToString();
                // Console.WriteLine(c.ToJsonString());
                if (RegExHelper.IsFinished(status))
                {

                    if(RegExHelper.IsReady(status))
                    {
                        return (c, null);
                    } 

                    // this means it is failed..
                    return (c, c.ToJsonString());
                    

                }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            return (c, "timedout");
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

                    // we will skip this for now..
                    if (c.Type.StartsWith("tls"))
                    {
                        continue;
                    }

                    if (!pairs.TryGetValue(k, out var g))
                    {
                        g = new AcmeChallengeGroup(auth.Identifier.Value, k, auth);
                        pairs.Add(k, g);
                    }
                    c.KeyAuthorization = $"{c.Token}.{keysum}";
                    if (hasWildcard)
                    {
                        if (c.Type.StartsWith("dns-01", StringComparison.OrdinalIgnoreCase))
                        {
                            c.KeyAuthorization = Signer.SHA256Base64Url(c.KeyAuthorization);
                            g.Challenges.Add(c);
                        }
                        continue;
                    }
                    if (c.Type.StartsWith("dns", StringComparison.OrdinalIgnoreCase))
                    {
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