using System;
using System.Security.Cryptography;
using System.Text;

namespace DotNetAcmeClient;

// Helper class for DNS validation
public class DnsValidationHelper
{
    public static string GetDnsRecordValue(string keyAuthorization)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyAuthorization));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
