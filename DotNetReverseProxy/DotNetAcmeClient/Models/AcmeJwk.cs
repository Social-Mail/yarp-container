using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DotNetAcmeClient.Models;

public class AcmeJwk
{
    public string e {get;set;}

    public string crv {get;set;}

    public string kty {get;set;}


    public string n {get;set;}

    public AcmeJwk(RSA key)
    {
        var rsaParams = key.ExportParameters(false);
        string eBase64Url = Base64UrlEncoder.Encode(rsaParams.Exponent);
        string nBase64Url = Base64UrlEncoder.Encode(rsaParams.Modulus);

        kty = "RSA";
        e = eBase64Url;
        n = nBase64Url;
        
        // string crv = rsaParams.Curve.Oid.FriendlyName switch
        // {
        //     "nistP256" or "ECDSA_P256" => "P-256",
        //     "nistP384" or "ECDSA_P384" => "P-384",
        //     "nistP521" or "ECDSA_P521" => "P-521",
        //     _ => throw new NotSupportedException($"Curve {parameters.Curve.Oid.FriendlyName} is not supported.")
        // };        
    }
}