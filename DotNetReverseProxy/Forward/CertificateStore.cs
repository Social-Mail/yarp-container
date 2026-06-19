using DnsClientX;
using DotNetAcmeClient;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;
using System.Linq;
using System.Net;
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
        FileEx.EnsureDirectory(this.localStorePath);
    }


    internal Task<X509Certificate2> GetAsync(string serverName)
    {
        serverName = serverName.ToLower();
        /// It is important to cache this for 15 minutes
        /// So even in case of DDOS, we are not going to forward it further
        return cache.GetOrCreate($"certificate-store-{serverName}", (c) =>
        {
            c.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return _GetAsync(serverName);
        })!;
    }
    internal async Task<X509Certificate2> _GetAsync(string serverName)
    {
        if (!await Resolves(serverName))
        {
            throw new InvalidOperationException($"{serverName} does not resolve to this server.");
        }

        // need file lock to prevent multiple installation...
        using var _lock = await LockFile.LockAsync($"get-or-install-server-certificate-{serverName}");

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

        var originalName = serverName;

        try {

            // install and save...

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
            return Create24HourCertificate(originalName);
        }
    }

    private async Task<bool> HasDnsForward(string serverName)
    {
        var cnameFrom = $"_acme-challenge." + serverName;
        var cnameTo = $"{serverName}{this.awsZoneSuffix}";

        // check CNAME for wildcard...
        var host = await ClientX.QueryDns(cnameFrom, DnsRecordType.CNAME, DnsEndpoint.GoogleQuic);
        if (host == null)
        {
            return false;
        }
        return host.Answers.Any((a) => a.Data == cnameTo);
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

    public static X509Certificate2 Create24HourCertificate(string subjectName)
    {
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

            // 5. Define validity period: Start now, expire in exactly 24 hours
            DateTimeOffset notBefore = DateTimeOffset.UtcNow;
            DateTimeOffset notAfter = notBefore.AddHours(24);

            // 6. Create the self-signed certificate
            return request.CreateSelfSigned(notBefore, notAfter);
        }
    }
}
