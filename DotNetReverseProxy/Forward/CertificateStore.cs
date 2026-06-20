using DnsClientX;
using DotNetAcmeClient;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DotNetReverseProxy.Forward;

class CertInfo
{
    public string Cert { get; set; }

    public string Key { get; set; }
}

public class CertificateStore
{
    private readonly CertificateInstaller installer;
    private readonly JsonLogger logger;
    private readonly IPAddress[] SelfIPs;
    private readonly string localStorePath;
    private readonly string? awsZoneSuffix;
    private readonly bool selfSigned;
    private readonly IMemoryCache cache;

    public CertificateStore(CertificateInstaller installer, JsonLogger logger, IMemoryCache cache)
    {
        this.installer = installer;
        this.cache = cache;
        this.logger = logger;
        this.SelfIPs = (System.Environment.GetEnvironmentVariable("SELF_IPs") ?? "0.0.0.0")
                .Split(",", StringSplitOptions.RemoveEmptyEntries).Select((x) => IPAddress.Parse(x.Trim()))
                .ToArray();
        this.localStorePath = System.Environment.GetEnvironmentVariable("FORWARD_CERT_STORE") ?? "/cache/certs/";
        this.awsZoneSuffix = System.Environment.GetEnvironmentVariable("AWS_ZONE_SUFFIX");
        this.selfSigned = System.Environment.GetEnvironmentVariable("ACME_MODE")?.Equals("self-signed") ?? false;
        FileEx.EnsureDirectory(this.localStorePath);
    }


    internal Task<X509Certificate2> GetAsync(string serverName)
    {
        serverName = serverName.ToLower();
        /// It is important to cache this for 15 minutes
        /// So even in case of DDOS, we are not going to forward it further
        var key = $"certificate-store-{serverName}";
        return cache.GetOrCreate(key, (c) =>
        {
            lock(this) {
                return cache.GetOrCreate(key, (c) => {
                    c.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                    return _GetAsync(serverName);
                });
            }
        })!;
    }
    internal async Task<X509Certificate2> _GetAsync(string serverName)
    {
        var originalName = serverName;

        if (selfSigned)
        {
            return await Create24HourCertificate(serverName);
        }

        try {
            if (String.IsNullOrWhiteSpace(serverName) || !await Resolves(serverName))
            {
                throw new InvalidOperationException($"{serverName} does not resolve to this server.");
                // send self signed certificate...
            }

            var cert = await LoadCached(serverName);
            if (cert != null) {
                return cert;
            }



            // install and save...
            // need file lock to prevent multiple installation...
            using var _lock = await LockFile.LockAsync($"get-or-install-server-certificate-{serverName}");

            var certFileName = serverName;
            if (this.awsZoneSuffix != null) {
                if (await HasDnsForward(serverName))
                {

                    certFileName = WildcardHelper.ReplaceAsFileName(serverName)!;
                    serverName = WildcardHelper.Replace(serverName);
                }
            }

            var installed = await installer.InstallCertificateAsync(serverName);
            await SaveCertToFile(certFileName, installed);
            return X509Certificate2.CreateFromPem(installed.Cert, installed.Key);
        } catch (Exception ex)
        {
            logger.LogError(new {
                error = ex.ToString(),
                host = serverName
            });
            return await Create24HourCertificate(originalName);
        }
    }

    private async Task<X509Certificate2?> LoadCached(string serverName)
    {
        var cert = await LoadCertFromFile(serverName);
        if (cert != null)
        {
            return cert;
        }

        var wildcard = WildcardHelper.ReplaceAsFileName(serverName);
        if (wildcard != null)
        {
            cert = await LoadCertFromFile(wildcard);
            if (cert != null)
            {
                return cert;
            }
        }
        return null;
    }

    private async Task<bool> HasDnsForward(string serverName)
    {

        // we need to go up...
        var root = WildcardHelper.GetTopLevel(serverName);

        var cnameFrom = $"_acme-challenge." + root;
        var cnameTo = $"{root}{this.awsZoneSuffix}";
        var cnameToDot = $"{root}{this.awsZoneSuffix}.";


        // check CNAME for wildcard...
        var host = await ClientX.QueryDns(cnameFrom, DnsRecordType.CNAME, DnsEndpoint.Cloudflare);
        if (host == null)
        {
            Console.WriteLine($"No Dns Entry {cnameFrom} -> {cnameTo}");
            return false;
        }
        var r = host.Answers.Any((a) => a.Data == cnameTo || a.Data == cnameToDot);
        if (!r)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                error= "no match",
                result = host,
                from = cnameFrom,
                to = cnameTo
            }));
        }
        return r;
    }

    private async Task<X509Certificate2?> LoadCertFromFile(string serverName)
    {
        var folder = Path.Join(this.localStorePath, serverName);
        var certPath = Path.Join(folder, "cert.pem");
        var keyPath = Path.Combine(folder, "key.pem");
        if (!File.Exists(certPath) || !File.Exists(keyPath)) {
            return null;
        }
        var cert = await File.ReadAllTextAsync(certPath);
        var key = await File.ReadAllTextAsync(keyPath);
        var xCert = X509Certificate2.CreateFromPem(cert, key);
        if(xCert.NotAfter < DateTime.UtcNow.AddDays(5))
        {
            // delete certs...
            File.Delete(certPath);
            File.Delete(keyPath);
            return null;
        }
        return xCert;
    }

    private async Task SaveCertToFile(string serverName, CertificateInfo cert)
    {
        var folder = Path.Join(this.localStorePath, serverName);
        FileEx.EnsureDirectory(folder);
        var certPath = Path.Join(folder, "cert.pem");
        var keyPath = Path.Combine(folder, "key.pem");
        await File.WriteAllTextAsync(certPath, cert.Cert);
        await File.WriteAllTextAsync(keyPath, cert.Key);
    }

    async Task<bool> Resolves(string serverName)
    {
        // this must verify the ip binding...
        var ip = await Dns.GetHostEntryAsync(serverName);
        foreach (var selfIp in SelfIPs)
        {
            foreach (var ipa in ip.AddressList)
            {
                if (ipa.Equals(selfIp))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public async Task<X509Certificate2> Create24HourCertificate(string subjectName)
    {

        var certFileName = "self-signed-" + subjectName;

        var xCert = await LoadCertFromFile(certFileName);
        if(xCert != null)
        {
            return xCert;
        }

        // 1. Generate RSA key pair (2048-bit is standard)
        using (RSA rsa = RSA.Create(2048))
        {
            // 2. Define the distinguished name (Subject and Issuer will be identical)
            var x500DistinguishedName = new X500DistinguishedName($"CN={subjectName}");

            // 3. Create the Certificate Request
            var request = new CertificateRequest(
                x500DistinguishedName, 
                rsa, 
                HashAlgorithmName.SHA256, 
                RSASignaturePadding.Pkcs1
            );

            // 4. Add standard Extensions (e.g., Basic Constraints, Key Usage)
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false)
            );

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, 
                    false
                )
            );

            // 5. Define validity period: Start now, expire in exactly 30 days
            DateTimeOffset notBefore = DateTimeOffset.UtcNow;
            DateTimeOffset notAfter = notBefore.AddDays(30);

            // 6. Create the self-signed certificate
            xCert = request.CreateSelfSigned(notBefore, notAfter);
            var cert = xCert.ExportCertificatePem();
            var keyPem = rsa.ExportRSAPrivateKeyPem();
            await SaveCertToFile(certFileName, new CertificateInfo { Cert= cert, Key = keyPem });
            return xCert;
        }
    }
}
