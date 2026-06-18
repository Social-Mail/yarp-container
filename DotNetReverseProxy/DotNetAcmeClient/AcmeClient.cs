using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using RetroCoreFit;
using System.Linq;

namespace DotNetAcmeClient;

public partial class AcmeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _directoryUrl;
    private readonly string _externalKid;
    private readonly string _externalHmacKey;
    private RSA _accountKey;
    private string _accountUrl;
    private AcmeDirectory _directory;
    private string _accountKeyJwk;
    private JsonWebKey? jwk;
    private string _currentNonce;

    private System.Text.Json.JsonSerializerOptions jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public AcmeClient(HttpClient httpClient, string directoryUrl, string accountKeyPath)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));

        // Set up default headers for ACME requests
        SetupDefaultHeaders();

        // Load existing account key or create new one
        LoadOrCreateAccountKey(accountKeyPath ?? throw new ArgumentNullException(nameof(accountKeyPath)));
    }

    public AcmeClient(HttpClient httpClient, string directoryUrl, string externalAccountBindingKID, string externalAccountBindingHmacKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));
        _externalKid = externalAccountBindingKID ?? throw new ArgumentNullException(nameof(externalAccountBindingKID));
        _externalHmacKey= externalAccountBindingHmacKey ?? throw new ArgumentNullException(nameof(externalAccountBindingHmacKey));
    }

    private void SetupDefaultHeaders()
    {
        // Set default Accept header for ACME
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Set User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ACME-Client/1.0");
    }

    private void LoadOrCreateAccountKey(string _accountKeyPath)
    {
        if (File.Exists(_accountKeyPath))
        {
            // Load existing account key
            try
            {
                var pemContent = File.ReadAllText(_accountKeyPath);
                _accountKey = RSA.Create();
                var keyBytes = Convert.FromBase64String(pemContent);
                _accountKey.ImportRSAPrivateKey(keyBytes, out _);
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load account key from {_accountKeyPath}: {ex.Message}", ex);
            }
        }
        else
        {
            // Create new account key
            _accountKey = RSA.Create(2048);
            var privateKeyBytes = _accountKey.ExportRSAPrivateKey();
            var pemContent = Convert.ToBase64String(privateKeyBytes);
            File.WriteAllText(_accountKeyPath, pemContent);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _directory = await GetDirectoryAsync(cancellationToken);
        await EnsureAccountExistsAsync(cancellationToken);
    }

    private async Task<AcmeDirectory> GetDirectoryAsync(CancellationToken cancellationToken)
    {
        var request = RequestBuilder.Get(_directoryUrl);
        return await request.GetResponseAsync<AcmeDirectory>(_httpClient, cancellationToken);
    }

    private async Task EnsureAccountExistsAsync(CancellationToken cancellationToken)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            termsOfServiceAgreed = true,
            contact = new[] { "mailto:admin@example.com" }
        });

        var signature = CreateJwsSignature(payload, _directory.NewAccount);

        var request = RequestBuilder.Post(_directory.NewAccount)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature)
            .Body(payload);

        var response = await request.GetResponseAsync<AcmeAccount>(_httpClient, cancellationToken);
        _accountUrl = response.Location;
    }


    public async Task<AcmeOrder> CreateOrderAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var identifiers = new List<object>();
        foreach (var domain in domains)
        {
            identifiers.Add(new { type = "dns", value = domain });
        }

        var payload = JsonSerializer.Serialize(new { identifiers });
        var signature = CreateJwsSignature(payload, _directory.NewOrder);

        var request = RequestBuilder.Post(_directory.NewOrder)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature)
            .Body(payload);

        var order = await request.GetResponseAsync<AcmeOrder>(_httpClient, cancellationToken);
        return order;
    }

    public async Task<AcmeAuthorization> GetAuthorizationAsync(string authorizationUrl, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = "{}";
        var signature = CreateJwsSignature(payload, authorizationUrl);

        var request = RequestBuilder.Get(authorizationUrl)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature);

        return await request.GetResponseAsync<AcmeAuthorization>(_httpClient, cancellationToken);
    }

    public async Task<AcmeChallenge> GetChallengeAsync(string challengeUrl, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = "{}";
        var signature = CreateJwsSignature(payload, challengeUrl);

        var request = RequestBuilder.Get(challengeUrl)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature);

        return await request.GetResponseAsync<AcmeChallenge>(_httpClient, cancellationToken);
    }

    public async Task CompleteChallengeAsync(string challengeUrl, string keyAuthorization, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(new { keyAuthorization });
        var signature = CreateJwsSignature(payload, challengeUrl);

        var request = RequestBuilder.Post(challengeUrl)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature)
            .Body(payload);

        await request.GetResponseAsync(_httpClient, cancellationToken);
    }

    public async Task<AcmeOrder> FinalizeOrderAsync(string orderUrl, string csr, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(new { csr });
        var signature = CreateJwsSignature(payload, orderUrl);

        var request = RequestBuilder.Post(orderUrl)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature)
            .Body(payload);

        return await request.GetResponseAsync<AcmeOrder>(_httpClient, cancellationToken);
    }

    public async Task<string> DownloadCertificateAsync(string certificateUrl, CancellationToken cancellationToken = default)
    {
        // First get a nonce
        await GetNonceAsync(cancellationToken);

        var payload = "{}";
        var signature = CreateJwsSignature(payload, certificateUrl);

        var request = RequestBuilder.Get(certificateUrl)
            .Header("Content-Type", "application/jose+json")
            .Header("Signature", signature);

        var response = await request.GetResponseAsync<AcmeCertificate>(_httpClient, cancellationToken);
        return response.Certificate;
    }

    public RSA GetAccountKey()
    {
        return _accountKey;
    }

    public string GetAccountKeyPem()
    {
        var privateKeyBytes = _accountKey.ExportRSAPrivateKey();
        return Convert.ToBase64String(privateKeyBytes);
    }

    // Get nonce from ACME server
    private async Task GetNonceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = RequestBuilder.Get(_directory.NewNonce);
            var response = await request.GetResponseAsync(_httpClient, cancellationToken);
            _currentNonce = response.Headers.GetValues("Replay-Nonce").FirstOrDefault();
        }
        catch
        {
            // If nonce endpoint fails, we'll handle it in the requests
        }
    }

    // Helper method to create JWK for signing
    private JsonWebKey GetJwk()
    {
        if (this.jwk != null)
            return jwk;

        var rsaParams = _accountKey.ExportParameters(false);
        jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsaParams));
        return jwk;
    }

    //// Helper method to create JWS signature for ACME requests
    //public string CreateJwsSignature(string payload, string url)
    //{
    //    var jwk = GetJwk();

    //    // Use the current nonce or fallback to a placeholder
    //    var nonce = _currentNonce ?? "nonce-placeholder";

    //    var protectedHeader = new
    //    {
    //        alg = "RS256",
    //        jwk = JsonSerializer.Deserialize<JsonElement>(jwk),
    //        url = url,
    //        nonce = nonce
    //    };

    //    var protectedHeaderB64 = Base64UrlEncode(JsonSerializer.Serialize(protectedHeader));
    //    var payloadB64 = Base64UrlEncode(payload);
    //    var signingInput = $"{protectedHeaderB64}.{payloadB64}";

    //    using var sha256 = SHA256.Create();
    //    var signatureBytes = _accountKey.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    //    var signatureB64 = Base64UrlEncode(signatureBytes);

    //    return $"{protectedHeaderB64}.{payloadB64}.{signatureB64}";
    //}

    private string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private string Base64UrlEncode(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}

// Data Models
public class AcmeDirectory
{
    public string NewAccount { get; set; }
    public string NewOrder { get; set; }
    public string NewNonce { get; set; }
    public string RevokeCert { get; set; }
    public string KeyChange { get; set; }
    public string Meta { get; set; }
}

public class AcmeAccount
{
    public string Location { get; set; }
    public string Status { get; set; }
    public string[] Contact { get; set; }
    public string TermsOfServiceAgreed { get; set; }
}

public class AcmeOrder
{
    public string Status { get; set; }
    public string[] Identifiers { get; set; }
    public string NotBefore { get; set; }
    public string NotAfter { get; set; }
    public string Certificate { get; set; }
    public string[] Authorizations { get; set; }
    public string Finalize { get; set; }
    public string Location { get; set; }
}

public class AcmeAuthorization
{
    public string Status { get; set; }
    public string Identifier { get; set; }
    public string[] Challenges { get; set; }
    public string Expires { get; set; }
}

public class AcmeChallenge
{
    public string Type { get; set; }
    public string Status { get; set; }
    public string Uri { get; set; }
    public string Token { get; set; }
    public string KeyAuthorization { get; set; }
}

public class AcmeCertificate
{
    public string Certificate { get; set; }
}

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

// Helper class for HTTP validation
public class HttpValidationHelper
{
    public static string GetHttpContent(string keyAuthorization)
    {
        return keyAuthorization;
    }
}
