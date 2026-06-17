using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using RetroCoreFit;
using System.Linq;

namespace DotNetReverseProxy;

public class AcmeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _directoryUrl;
    private readonly RSA _accountKey;
    private readonly string _accountKeyPath;
    private string _accountUrl;
    private AcmeDirectory _directory;
    private string _accountKeyJwk;
    private string _currentNonce;

    public AcmeClient(HttpClient httpClient, string directoryUrl, RSA accountKey, string accountKeyPath = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));
        _accountKey = accountKey ?? throw new ArgumentNullException(nameof(accountKey));
        _accountKeyPath = accountKeyPath;
        
        // Set up default headers for ACME requests
        SetupDefaultHeaders();
        
        // Save account key if path is provided
        if (!string.IsNullOrEmpty(_accountKeyPath))
        {
            SaveAccountKey();
        }
    }

    public AcmeClient(HttpClient httpClient, string directoryUrl, string accountKeyPem = null, string accountKeyPath = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));
        _accountKeyPath = accountKeyPath;
        
        if (!string.IsNullOrEmpty(accountKeyPem))
        {
            _accountKey = RSA.Create();
            _accountKey.ImportFromPem(accountKeyPem);
        }
        else
        {
            _accountKey = RSA.Create(2048);
        }
        
        // Set up default headers for ACME requests
        SetupDefaultHeaders();
        
        // Save account key if path is provided
        if (!string.IsNullOrEmpty(_accountKeyPath))
        {
            SaveAccountKey();
        }
    }

    public AcmeClient(HttpClient httpClient, string directoryUrl, string accountKeyPath)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));
        _accountKeyPath = accountKeyPath;
        
        // Load existing account key
        _accountKey = LoadAccountKey();
        
        // Set up default headers for ACME requests
        SetupDefaultHeaders();
    }

    private void SetupDefaultHeaders()
    {
        // Set default Accept header for ACME
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        // Set User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ACME-Client/1.0");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _directory = await GetDirectoryAsync(cancellationToken);
        await EnsureAccountExistsAsync(cancellationToken);
    }

    public static RSA GenerateAccountKey(int keySize = 2048)
    {
        return RSA.Create(keySize);
    }

    public static string ExportAccountKeyToPem(RSA rsaKey)
    {
        var privateKeyBytes = rsaKey.ExportRSAPrivateKey();
        return Convert.ToBase64String(privateKeyBytes);
    }

    public static RSA ImportAccountKeyFromPem(string pemData)
    {
        var keyBytes = Convert.FromBase64String(pemData);
        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(keyBytes, out _);
        return rsa;
    }

    public static RSA ImportAccountKeyFromPemFile(string filePath)
    {
        var pemContent = File.ReadAllText(filePath);
        return ImportAccountKeyFromPem(pemContent);
    }

    public static void ExportAccountKeyToPemFile(RSA rsaKey, string filePath)
    {
        var pemContent = ExportAccountKeyToPem(rsaKey);
        File.WriteAllText(filePath, pemContent);
    }

    private void SaveAccountKey()
    {
        if (!string.IsNullOrEmpty(_accountKeyPath))
        {
            try
            {
                ExportAccountKeyToPemFile(_accountKey, _accountKeyPath);
            }
            catch (Exception ex)
            {
                // Log or handle the exception as appropriate
                System.Diagnostics.Debug.WriteLine($"Failed to save account key: {ex.Message}");
            }
        }
    }

    private RSA LoadAccountKey()
    {
        if (string.IsNullOrEmpty(_accountKeyPath))
            throw new ArgumentException("Account key path must be provided to load an existing key", nameof(_accountKeyPath));

        if (!File.Exists(_accountKeyPath))
            throw new FileNotFoundException($"Account key file not found: {_accountKeyPath}");

        try
        {
            return ImportAccountKeyFromPemFile(_accountKeyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load account key from {_accountKeyPath}: {ex.Message}", ex);
        }
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
        
        var payload = JsonSerializer.Serialize(new { 
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
        
        // Save the account key if we have a path
        if (!string.IsNullOrEmpty(_accountKeyPath))
        {
            SaveAccountKey();
        }
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
        return ExportAccountKeyToPem(_accountKey);
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
    private string GetJwk()
    {
        if (!string.IsNullOrEmpty(_accountKeyJwk))
            return _accountKeyJwk;

        var rsaParams = _accountKey.ExportParameters(false);
        var jwk = new
        {
            kty = "RSA",
            n = Base64UrlEncode(rsaParams.Modulus),
            e = Base64UrlEncode(rsaParams.Exponent)
        };

        _accountKeyJwk = JsonSerializer.Serialize(jwk);
        return _accountKeyJwk;
    }

    // Helper method to create JWS signature for ACME requests
    public string CreateJwsSignature(string payload, string url)
    {
        var jwk = GetJwk();
        
        // Use the current nonce or fallback to a placeholder
        var nonce = _currentNonce ?? "nonce-placeholder";
        
        var protectedHeader = new
        {
            alg = "RS256",
            jwk = JsonSerializer.Deserialize<JsonElement>(jwk),
            url = url,
            nonce = nonce
        };

        var protectedHeaderB64 = Base64UrlEncode(JsonSerializer.Serialize(protectedHeader));
        var payloadB64 = Base64UrlEncode(payload);
        var signingInput = $"{protectedHeaderB64}.{payloadB64}";

        using var sha256 = SHA256.Create();
        var signatureBytes = _accountKey.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureB64 = Base64UrlEncode(signatureBytes);

        return $"{protectedHeaderB64}.{payloadB64}.{signatureB64}";
    }

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
