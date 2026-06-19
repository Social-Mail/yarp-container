using DnsClientX;
using DotNetAcmeClient;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System;
using System.IO;
using System.Linq;
using System.Net;
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
    private readonly IPAddress[] SelfIPs;
    private readonly string localStorePath;
    private readonly string? awsZoneSuffix;

    public CertificateStore(CertificateInstaller installer)
    {
        this.installer = installer;
        this.SelfIPs = (System.Environment.GetEnvironmentVariable("SELF_IPs") ?? "0.0.0.0")
                .Split(",", StringSplitOptions.RemoveEmptyEntries).Select((x) => IPAddress.Parse(x.Trim()))
                .ToArray();
        this.localStorePath = System.Environment.GetEnvironmentVariable("FORWARD_CERT_STORE") ?? "/cache/certs/";
        this.awsZoneSuffix = System.Environment.GetEnvironmentVariable("AWS_ZONE_SUFFIX");
        FileEx.EnsureDirectory(this.localStorePath);
    }

    internal async Task<X509Certificate2> GetAsync(string serverName)
    {
        if (!await Resolves(serverName))
        {
            throw new InvalidOperationException($"{serverName} does not resolve to this server.");
        }

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

        // install and save...
        // need file lock to prevent multiple installation...
        using var _lock = await LockFile.LockAsync(serverName);

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
        if(xCert.NotAfter > DateTime.UtcNow.AddDays(1))
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
}
