using Microsoft.AspNetCore.Http.Json;
using Microsoft.IdentityModel.Tokens;
using RetroCoreFit;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetAcmeClient;



partial class AcmeClient
{


    public Task<RequestBuilder> ApiRequest<T>(string url, T payload, CancellationToken cancellationToken, bool includeJwsKid = true, bool includeExternalAccountBinding = false)
    {
        var kid = includeJwsKid ? this._accountUrl : null;
        return SignedRequest<T>(url, payload, cancellationToken, kid:kid, includeExternalAccountBinding: includeExternalAccountBinding);
    }

    public async Task<RequestBuilder> SignedRequest<T>(string url, T payload, CancellationToken cancellationToken, string? kid = null, string? nonce = null, bool includeExternalAccountBinding = false)
    {

        // Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        // {
        //     url,
        //     kid,
        //     includeExternalAccountBinding
        // }));

        if (nonce == null)
        {
            var request = RequestBuilder.Get(_directory.NewNonce);
            var response = await request.GetResponseAsync(_httpClient, cancellationToken);
            nonce = response.Headers.GetValues("Replay-Nonce").FirstOrDefault();
        }


        JsonObject node = (System.Text.Json.JsonSerializer.SerializeToNode(payload) as JsonObject)!;

        if (includeExternalAccountBinding && _externalKid != null)
        {
            var jwk = this.GetJwk();

            // this is trickey part, we need to convert to JsonElement and inject property
            node.Add("externalAccountBinding", CreateSignedHmacBody(_externalHmacKey, url, jwk, kid: _externalKid));
        }


        var data = CreateSignedBody(url, node, kid: kid, nonce: nonce);

        var json = System.Text.Json.JsonSerializer.Serialize(data);

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/jose+json");
        content.Headers.ContentType.CharSet = "";

        return RequestBuilder.Post(url)
            .Content(content);

        JsonObject CreateSignedBody(string url, JsonObject payload, string? kid = null, string? nonce = null)
        {
            var jwk = this.GetJwk();
            var headerAlg = "RS256";
            var signerAlg = "SHA256";
            if (jwk.crv != null && jwk.kty.Equals("KC", StringComparison.OrdinalIgnoreCase))
            {
                headerAlg = "ES256";
                if (jwk.crv == "P-384")
                {
                    headerAlg = "ES384";
                    signerAlg = "SHA384";
                }
                else if (jwk.crv == "P-521" || jwk.crv == "P-512")
                {
                    headerAlg = "ES512";
                    signerAlg = "SHA512";
                }
            }

            var result = PrepareSignedBody(headerAlg, url, payload, kid: kid, nonce: nonce);

            var @protected = (result["protected"] as JsonValue)!.ToString();
            var p = (result["payload"] as JsonValue)!.ToString();

            var signature = SignJws(signerAlg, @protected, p, this._accountKey);
            result.Add("signature", signature);

            return result;
        }

        JsonObject PrepareSignedBody(string alg, string url, JsonObject payload, string? kid = null, string? nonce = null)
        {
            AcmeJwk? jwk = null;
            if (kid == null)
            {
                jwk = this.GetJwk();
            }
            var header = new
            {
                alg,
                url,
                nonce,
                kid,
                jwk
            };

            return (System.Text.Json.JsonSerializer.SerializeToNode(new
            {
                payload = payload == null ? "" : Base64UrlEncoder.Encode(payload.ToJsonString(jsonOptions)),
                @protected = Base64UrlEncoder.Encode(System.Text.Json.JsonSerializer.Serialize(header, jsonOptions))
            }, jsonOptions) as JsonObject)!;

        }

        JsonNode CreateSignedHmacBody(string hmacKey, string url, AcmeJwk jwk, string? kid = null, string? nonce = null)
        {
            var payload = System.Text.Json.JsonSerializer.SerializeToNode(jwk, jsonOptions) as JsonObject;
            var result = PrepareSignedBody("HS256", url, payload, kid, nonce);

            var key = Convert.FromBase64String(hmacKey);

            var utf8 = System.Text.Encoding.UTF8;

            var @protected = (result["protected"] as JsonValue)!.ToString();
            var p = (result["payload"] as JsonValue)!.ToString();

            var signature = HMACSHA256.HashData(key, utf8.GetBytes($"{@protected}.{p}"));

            result.Add("signature", Base64UrlEncoder.Encode(signature));

            return result;
        }

        static string SignJws(string signerAlg, string protectedHeader, string payload, object accountKey)
        {
            // 1. Replicate .update(`${result.protected}.${result.payload}`, 'utf8')
            string dataToSign = $"{protectedHeader}.{payload}";
            byte[] dataBytes = Encoding.UTF8.GetBytes(dataToSign);

            // 2. Map the hash algorithm string
            HashAlgorithmName hashAlg = signerAlg switch
            {
                "SHA256" => HashAlgorithmName.SHA256,
                "SHA384" => HashAlgorithmName.SHA384,
                "SHA512" => HashAlgorithmName.SHA512,
                _ => throw new NotSupportedException($"Algorithm {signerAlg} is not supported.")
            };

            // 3. Replicate .sign() matching the padding and format
            byte[] rawSignature = accountKey switch
            {
                // RSA handles RSA_PKCS1_PADDING natively via RSASignaturePadding.Pkcs1
                RSA rsaKey => rsaKey.SignData(dataBytes, hashAlg, RSASignaturePadding.Pkcs1),

                // ECDsa natively outputs the 'ieee-p1363' (r, s concatenated) byte layout
                ECDsa ecdsaKey => ecdsaKey.SignData(dataBytes, hashAlg),

                _ => throw new ArgumentException("Unsupported account key type. Must be RSA or ECDsa.")
            };

            // 4. Replicate output format: 'base64url'
            return Convert.ToBase64String(rawSignature)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }

}

