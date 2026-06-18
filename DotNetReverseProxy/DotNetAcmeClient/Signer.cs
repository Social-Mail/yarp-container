using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DotNetAcmeClient;

public class Signer
{
    public static string SHA256Base64Url(string content)
    {
        return Base64UrlEncoder.Encode(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        content)));
    }
}